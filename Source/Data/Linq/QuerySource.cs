﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace BLToolkit.Data.Linq
{
	using Mapping;
	using Data.Sql;
	using Reflection;

	abstract class QuerySource : QueryField
	{
		public class Table : QuerySource
		{
			public Table(MappingSchema mappingSchema, SqlBuilder sqlBuilder, LambdaInfo lambda)
				: base(sqlBuilder, lambda)
			{
				ObjectType = ((IQueryable)((ConstantExpression)lambda.Body.Expr).Value).ElementType;
				SqlTable   = new SqlTable(mappingSchema, ObjectType);

				sqlBuilder.From.Table(SqlTable);

				var objectMapper = mappingSchema.GetObjectMapper(ObjectType);

				foreach (var field in SqlTable.Fields)
				{
					var mapper = objectMapper[field.Value.PhysicalName];
					_columns.Add(mapper.MemberAccessor.MemberInfo, new Column(this, field.Value, mapper));
				}
			}

			public Type     ObjectType;
			public SqlTable SqlTable;

			Dictionary<MemberInfo,Column> _columns = new Dictionary<MemberInfo,Column>();

			public override QueryField GetField(MemberInfo mi)
			{
				Column col;
				_columns.TryGetValue(mi, out col);
				return col;
			}

			public override QueryField GetField(Expression expr)
			{
				if (expr.NodeType == ExpressionType.MemberAccess)
				{
					var ma = (MemberExpression)expr;

					if (ma.Expression != null && ma.Expression.Type == ObjectType)
						return GetField(ma.Member);
				}

				return null;
			}

			protected override IEnumerable<QueryField> GetFields()
			{
				foreach (var col in _columns.Values)
					yield return col;
			}
		}

		public class Expr : QuerySource
		{
			public Expr(SqlBuilder sqlBilder, LambdaInfo lambda, params QuerySource[] parentQueries)
				: base(sqlBilder, lambda, parentQueries)
			{
				if (lambda.Body.Expr is NewExpression)
				{
					var ex = (NewExpression)lambda.Body.Expr;

					if (ex.Members == null)
						return;

					for (var i = 0; i < ex.Members.Count; i++)
					{
						var member = ex.Members[i];

						if (member is MethodInfo)
							member = TypeHelper.GetPropertyByMethod((MethodInfo)member);

						var field = 
							GetParentField(ex.Arguments[i]) ??
							new ExprColumn(this, lambda.Body.Create(ex.Arguments[i], lambda.Body.Index(ex.Arguments, New.Arguments, i)), member.Name);

						_fields.Add(member, field);
					}
				}
				else
				{
					var ex = (MemberInitExpression)lambda.Body.Expr;

					for (var i = 0; i < ex.Bindings.Count; i++)
					{
						var binding = ex.Bindings[i];
						var member  = binding.Member;

						if (member is MethodInfo)
							member = TypeHelper.GetPropertyByMethod((MethodInfo)member);

						if (binding is MemberAssignment)
						{
							var ma = binding as MemberAssignment;

							var piBinding    = lambda.Body.Create(ma.Expression, lambda.Body.Index(ex.Bindings, MemberInit.Bindings, i));
							var piAssign     = piBinding.  Create(ma.Expression, piBinding.ConvertExpressionTo<MemberAssignment>());
							var piExpression = piAssign.   Create(ma.Expression, piAssign.Property(MemberAssignmentBind.Expression));

							var field = GetParentField(piExpression) ?? new ExprColumn(this, piExpression, member.Name);

							_fields.Add(member, field);
						}
						else
							throw new InvalidOperationException();
					}
				}
			}

			Dictionary<MemberInfo,QueryField> _fields = new Dictionary<MemberInfo,QueryField>();

			public override QueryField GetField(MemberInfo mi)
			{
				QueryField fld;
				_fields.TryGetValue(mi, out fld);
				return fld;
			}

			public override QueryField GetField(Expression expr)
			{
				switch (expr.NodeType)
				{
					case ExpressionType.Parameter:
						throw new InvalidOperationException();
						//return this;

					case ExpressionType.MemberAccess:
						{
							var ma = (MemberExpression)expr;

							if (ma.Expression != null)
							{
								if (ma.Expression.NodeType == ExpressionType.Parameter)
									return GetField(ma.Member);

								if (ma.Expression.NodeType == ExpressionType.Constant)
									break;
							}

							var list = new List<MemberInfo>();

							while (expr != null)
							{
								switch (expr.NodeType)
								{
									case ExpressionType.MemberAccess:
										ma = (MemberExpression)expr;

										list.Insert(0, ma.Member);

										expr = ma.Expression;
										break;

									case ExpressionType.Parameter:
										expr = null;
										break;

									default:
										return null;
								}
							}

							var field = GetField(list, 0);

							if (field != null)
								return field;

							break;
						}
				}

				foreach (var item in _fields)
					if (item.Value is ExprColumn && ((ExprColumn)item.Value).Expr == expr)
						return item.Value;

				return null;
			}

			protected override IEnumerable<QueryField> GetFields()
			{
				return _fields.Values;
			}
		}

		public class SubQuery : QuerySource
		{
			public SubQuery(SqlBuilder currentSql, SqlBuilder subSql, QuerySource parentQuery)
				: base(currentSql, null, parentQuery)
			{
				SqlBuilder.From.Table(SubSql = subSql);

				foreach (var field in parentQuery.GetFields())
					GetColumn(field);
			}

			public SqlBuilder SubSql;

			Dictionary<QueryField,SubQueryColumn> _columns = new Dictionary<QueryField,SubQueryColumn>();

			public SubQueryColumn GetColumn(QueryField field)
			{
				if (field == null)
					return null;

				SubQueryColumn col;

				if (!_columns.TryGetValue(field, out col))
					_columns.Add(field, col = new SubQueryColumn(this, field));

				return col;
			}

			public override QueryField GetField(MemberInfo mi)
			{
				return GetColumn(ParentQueries[0].GetField(mi));
			}

			public override QueryField GetField(Expression expr)
			{
				return GetColumn(ParentQueries[0].GetField(expr));
			}

			protected override IEnumerable<QueryField> GetFields()
			{
				foreach (var col in _columns.Values)
					yield return col;
			}

			public override ISqlExpression GetExpression<T>(ExpressionParser<T> parser)
			{
				if (_columns.Count == 1 && ParentQueries[0] is Scalar)
					return _columns.Values.First().GetExpression(parser);

				throw new InvalidOperationException();
			}

			protected override QueryField GetField(List<MemberInfo> members, int currentMember)
			{
				var field = GetField(members[currentMember]);

				if (field == null || currentMember + 1 == members.Count)
					 return field;

				if (!(field is SubQueryColumn))
					return ((QuerySource)field).GetField(members, currentMember + 1);

				field = ((SubQueryColumn)field).Field;

				//if (field.Sources.Length != 1)
				//	throw new InvalidOperationException();

				field = ParentQueries[0].GetField(members, currentMember);
				//field = field.Sources[0].GetField(members, currentMember);

				return field != null ? GetColumn(field) : null;
			}
		}

		public class Scalar : QuerySource
		{
			public Scalar(SqlBuilder sqlBilder, LambdaInfo lambda, params QuerySource[] parentQueries)
				: base(sqlBilder, lambda, parentQueries)
			{
				_field = GetParentField(lambda.Body) ?? new ExprColumn(this, lambda.Body, null);
			}

			QueryField _field;

			public override QueryField GetField(MemberInfo mi)
			{
				throw new NotImplementedException();
			}

			public override QueryField GetField(Expression expr)
			{
				throw new NotImplementedException();
			}

			protected override IEnumerable<QueryField> GetFields()
			{
				yield return _field;
			}
		}

		protected QuerySource(SqlBuilder sqlBilder, LambdaInfo lambda, params QuerySource[] parentQueries)
		{
			SqlBuilder    = sqlBilder;
			Lambda        = lambda;
			ParentQueries = parentQueries;
		}

		public override QuerySource[] Sources { get { return ParentQueries; } }

		public QuerySource[] ParentQueries;
		public SqlBuilder    SqlBuilder;
		public LambdaInfo    Lambda;

		//readonly List<QueryField> _fields = new List<QueryField>();
		//public   List<QueryField>  Fields { get { return _fields; } }

		public    abstract QueryField              GetField(Expression expr);
		public    abstract QueryField              GetField(MemberInfo mi);
		protected abstract IEnumerable<QueryField> GetFields();

		protected virtual QueryField GetField(List<MemberInfo> members, int currentMember)
		{
			var field = GetField(members[currentMember]);

			if (field == null || currentMember + 1 == members.Count)
				 return field;

			return ((QuerySource)field).GetField(members, currentMember + 1);
		}

		public QueryField GetParentField(Expression expr)
		{
			if (ParentQueries.Length > 0)
			{
				if (expr.NodeType == ExpressionType.Parameter)
				{
					if (ParentQueries.Length == 1)
						return ParentQueries[0];

					if (ParentQueries.Length != Lambda.Parameters.Length)
						throw new InvalidOperationException();

					for (int i = 0; i < ParentQueries.Length; i++)
						if (Lambda.Parameters[i].Expr == expr)
							return ParentQueries[i];
				}

				foreach (var pq in ParentQueries)
				{
					var field = pq.GetField(expr);
					if (field != null)
						return field;
				}
			}

			return null;
		}

		FieldIndex[] _indexes;

		public override FieldIndex[] Select<T>(ExpressionParser<T> parser)
		{
			if (_indexes == null)
			{
				var fields = GetFields().ToList();

				_indexes = new FieldIndex[fields.Count];

				var i = 0;

				foreach (var field in fields)
				{
					var idx = field.Select(parser);

					if (idx.Length != 1)
						throw new InvalidOperationException();

					_indexes[i++] = new FieldIndex { Index = idx[0].Index, Field = field };
				}
			}

			return _indexes;
		}

		public override ISqlExpression GetExpression<T>(ExpressionParser<T> parser)
		{
			throw new InvalidOperationException();
		}

		public void Match(
			Action<Table>    tableAction,
			Action<Expr>     exprAction,
			Action<SubQuery> subQueryAction,
			Action<Scalar>   scalarAction)
		{
			if      (this is Table)    tableAction   (this as Table);
			else if (this is Expr)     exprAction    (this as Expr);
			else if (this is SubQuery) subQueryAction(this as SubQuery);
			else if (this is Scalar)   scalarAction  (this as Scalar);
		}
	}
}
