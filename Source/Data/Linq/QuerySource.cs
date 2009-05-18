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
				: base(sqlBuilder, lambda, null)
			{
				ObjectType = ((IQueryable)((ConstantExpression)lambda.Body.Expr).Value).ElementType;
				SqlTable   = new SqlTable(mappingSchema, ObjectType);

				sqlBuilder.From.Table(SqlTable);

				var objectMapper = mappingSchema.GetObjectMapper(ObjectType);

				foreach (var field in SqlTable.Fields)
				{
					var mapper = objectMapper[field.Value.PhysicalName];

					_fields.Add(mapper.MemberAccessor.MemberInfo, new Column(this, field.Value, mapper));
				}
			}

			public Type     ObjectType;
			public SqlTable SqlTable;
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

						QueryField field = null;

						foreach (var parentQuery in parentQueries)
						{
							field = parentQuery.GetField(ex.Arguments[i]);
							if (field != null)
								break;
						}

						if (field != null)
						{
							_fields.Add(member, field);
						}
						else
						{
							var e = new ExprColumn(this, lambda.Body.Create(ex.Arguments[i], lambda.Body.Index(ex.Arguments, New.Arguments, i)), member.Name);
							_fields.Add(member, e);
						}
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

							QueryField field = null;

							foreach (var parentQuery in parentQueries)
							{
								field = parentQuery.GetField(piExpression);
								if (field != null)
									break;
							}

							_fields.Add(member, field ?? new ExprColumn(this, piExpression, member.Name));
						}
						else
							throw new InvalidOperationException();
					}
				}
			}
		}

		public class SubQuery : QuerySource
		{
			public SubQuery(SqlBuilder subSql, QuerySource parentQuery)
				: base(new SqlBuilder(), null, parentQuery)
			{
				SqlBuilder.From.Table(SubSql = subSql);

				foreach (var field in parentQuery._fields)
					_fields.Add(field.Key, new SubQueryColumn(this, field.Value));
			}

			public SqlBuilder SubSql;
		}

		public class Scalar : QuerySource
		{
			public Scalar(SqlBuilder sqlBilder, LambdaInfo lambda, params QuerySource[] parentQueries)
				: base(sqlBilder, lambda, parentQueries)
			{
			}
		}

		protected QuerySource(SqlBuilder sqlBilder, LambdaInfo lambda, params QuerySource[] parentQueries)
		{
			SqlBuilder    = sqlBilder;
			Lambda        = lambda;
			ParentQueries = parentQueries;
		}

		public SqlBuilder    SqlBuilder;
		public QuerySource[] ParentQueries;
		public LambdaInfo    Lambda;

		readonly Dictionary<MemberInfo,QueryField> _fields = new Dictionary<MemberInfo, QueryField>();
		public   Dictionary<MemberInfo,QueryField>  Fields { get { return _fields; } }

		public virtual QueryField GetField(Expression expr)
		{
			switch (expr.NodeType)
			{
				case ExpressionType.Parameter:
					return this;

				case ExpressionType.MemberAccess:
					{
						var ma = (MemberExpression)expr;

						if (ma.Expression != null)
						{
							if (ma.Expression.NodeType == ExpressionType.Parameter)
							{
								QueryField fld;
								_fields.TryGetValue(ma.Member, out fld);
								return fld;
							}

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

						var        source = this;
						QueryField field  = null;

						for (var i = 0; i < list.Count; i++)
						{
							var mi = list[i];

							source._fields.TryGetValue(mi, out field);

							if (field == null || i + 1 == list.Count)
								 break;

							if (!(field is QuerySource))
								return null;

							source = (QuerySource)field;
						}

						return field;
					}
			}

			foreach (var item in _fields)
				if (item.Value is ExprColumn && ((ExprColumn)item.Value).Expr == expr)
					return item.Value;

			return null;
		}

		int[] _indexes;

		public override int[] Select<T>(ExpressionParser<T> parser)
		{
			if (_indexes == null)
			{
				_indexes = new int[_fields.Count];

				var i = 0;

				foreach (var field in _fields.Values)
					_indexes[i++] = field.Select(parser)[0];
			}

			return _indexes;
		}

		public override ISqlExpression GetExpression<T>(ExpressionParser<T> parser)
		{
			throw new InvalidOperationException();
			//throw new LinqException("Cannot convert '{0}' to SQL.", Field.GetExpression(parser));
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