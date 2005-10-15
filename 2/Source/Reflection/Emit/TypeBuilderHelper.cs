using System;
using System.Reflection;
using System.Reflection.Emit;

namespace BLToolkit.Reflection.Emit
{
	/// <summary>
	/// A wrapper around the <see cref="TypeBuilder"/> class.
	/// </summary>
	/// <include file="Examples.CS.xml" path='examples/emit[@name="Emit"]/*' />
	/// <include file="Examples.VB.xml" path='examples/emit[@name="Emit"]/*' />
	/// <seealso cref="System.Reflection.Emit.TypeBuilder">TypeBuilder Class</seealso>
	public class TypeBuilderHelper
	{
		/// <summary>
		/// Initializes a new instance of the <see cref="TypeBuilderHelper"/> class
		/// with the specified parameters.
		/// </summary>
		/// <param name="assemblyBuilder">Associated <see cref="AssemblyBuilderHelper"/>.</param>
		/// <param name="typeBuilder">A <see cref="TypeBuilder"/></param>
		public TypeBuilderHelper(AssemblyBuilderHelper assemblyBuilder, System.Reflection.Emit.TypeBuilder typeBuilder)
		{
			if (assemblyBuilder == null) throw new ArgumentNullException("assemblyBuilder");
			if (typeBuilder     == null) throw new ArgumentNullException("typeBuilder");

			_assembly    = assemblyBuilder;
			_typeBuilder = typeBuilder;

			_typeBuilder.SetCustomAttribute(_assembly.BLToolkitAttribute);
		}

		private AssemblyBuilderHelper _assembly;
		/// <summary>
		/// Gets associated AssemblyBuilderHelper.
		/// </summary>
		public  AssemblyBuilderHelper  Assembly
		{
			get { return _assembly; }
		}

		private System.Reflection.Emit.TypeBuilder _typeBuilder;
		/// <summary>
		/// Gets TypeBuilder.
		/// </summary>
		public  System.Reflection.Emit.TypeBuilder  TypeBuilder
		{
			get { return _typeBuilder; }
		}

		/// <summary>
		/// Converts the supplied <see cref="TypeBuilderHelper"/> to a <see cref="TypeBuilder"/>.
		/// </summary>
		/// <param name="typeBuilder">The TypeBuilderHelper.</param>
		/// <returns>A TypeBuilder.</returns>
		public static implicit operator System.Reflection.Emit.TypeBuilder(TypeBuilderHelper typeBuilder)
		{
			return typeBuilder.TypeBuilder;
		}

		#region DefineMethod Overrides

		/// <summary>
		/// Adds a new method to the class, with the given name and method signature.
		/// </summary>
		/// <param name="name">The name of the method. name cannot contain embedded nulls. </param>
		/// <param name="attributes">The attributes of the method. </param>
		/// <param name="returnType">The return type of the method.</param>
		/// <param name="parameterTypes">The types of the parameters of the method.</param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(
			string name, MethodAttributes attributes, Type returnType, Type[] parameterTypes)
		{
			return new MethodBuilderHelper(this, _typeBuilder.DefineMethod(name, attributes, returnType, parameterTypes));
		}

		/// <summary>
		/// Adds a new method to the class, with the given name and method signature.
		/// </summary>
		/// <param name="name">The name of the method. name cannot contain embedded nulls. </param>
		/// <param name="attributes">The attributes of the method. </param>
		/// <param name="returnType">The return type of the method.</param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(string name, MethodAttributes attributes, Type returnType)
		{
			return new MethodBuilderHelper(this, _typeBuilder.DefineMethod(name, attributes, returnType, Type.EmptyTypes));
		}

		/// <summary>
		/// Adds a new method to the class, with the given name and method signature.
		/// </summary>
		/// <param name="name">The name of the method. name cannot contain embedded nulls. </param>
		/// <param name="attributes">The attributes of the method. </param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(string name, MethodAttributes attributes)
		{
			return new MethodBuilderHelper(this, _typeBuilder.DefineMethod(name, attributes, typeof(void), Type.EmptyTypes));
		}

		/// <summary>
		/// Adds a new method to the class, with the given name and method signature.
		/// </summary>
		/// <param name="name">The name of the method. name cannot contain embedded nulls. </param>
		/// <param name="methodInfoDeclaration">The method whose declaration is to be used.</param>
		/// <param name="attributes">The attributes of the method. </param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(
			string name, MethodInfo methodInfoDeclaration, MethodAttributes attributes)
		{
			ParameterInfo[] pi = methodInfoDeclaration.GetParameters();
			Type[]  parameters = new Type[pi.Length];

			for (int i = 0; i < pi.Length; i++)
				parameters[i] = pi[i].ParameterType;

			MethodBuilderHelper method = DefineMethod(
				name, attributes | MethodAttributes.Virtual, methodInfoDeclaration.ReturnType, parameters);

			_typeBuilder.DefineMethodOverride(method.MethodBuilder, methodInfoDeclaration);

			return method;
		}

		/// <summary>
		/// Adds a new method to the class, with the given name and method signature.
		/// </summary>
		/// <param name="name">The name of the method. name cannot contain embedded nulls. </param>
		/// <param name="methodInfoDeclaration">The method whose declaration is to be used.</param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(string name, MethodInfo methodInfoDeclaration)
		{
			return DefineMethod(name, methodInfoDeclaration, MethodAttributes.Virtual);
		}

		/// <summary>
		/// Adds a new method to the class.
		/// </summary>
		/// <param name="methodInfoDeclaration">The method whose declaration is to be used.</param>
		/// <returns>The defined method.</returns>
		public MethodBuilderHelper DefineMethod(MethodInfo methodInfoDeclaration)
		{
			return DefineMethod(
				methodInfoDeclaration.DeclaringType.FullName + "." + methodInfoDeclaration.Name,
				methodInfoDeclaration,
				MethodAttributes.Virtual);
		}

		#endregion

		/// <summary>
		/// Creates a Type object for the class.
		/// </summary>
		/// <returns>Returns the new Type object for this class.</returns>
		public Type Create()
		{
			return TypeBuilder.CreateType();
		}

		/// <summary>
		/// Sets a custom attribute.
		/// </summary>
		/// <param name="attributeType">Attribute type</param>
		public void SetCustomAttribute(Type attributeType)
		{
			ConstructorInfo        ci        = attributeType.GetConstructor(Type.EmptyTypes);
			CustomAttributeBuilder caBuilder = new CustomAttributeBuilder(ci, new object[0]);

			_typeBuilder.SetCustomAttribute(caBuilder);
		}

		private ConstructorBuilderHelper _typeInitializer;
		/// <summary>
		/// Gets the initializer for this type.
		/// </summary>
		public ConstructorBuilderHelper TypeInitializer
		{
			get 
			{
				if (_typeInitializer == null)
					_typeInitializer = new ConstructorBuilderHelper(this, _typeBuilder.DefineTypeInitializer());

				return _typeInitializer;
			}
		}

		private ConstructorBuilderHelper _defaultConstructor;
		/// <summary>
		/// Gets the initializer for this type.
		/// </summary>
		public ConstructorBuilderHelper DefaultConstructor
		{
			get 
			{
				if (_defaultConstructor == null)
					_defaultConstructor = new ConstructorBuilderHelper(
						this, _typeBuilder.DefineDefaultConstructor(MethodAttributes.Public));

				return _typeInitializer;
			}
		}
	}
}