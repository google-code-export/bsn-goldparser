using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;

namespace bsn.GoldParser.Semantic {
	internal static class SemanticNonterminalTypeFactoryHelper {
		public delegate T Activator<T>(ReadOnlyCollection<SemanticToken> tokens);

		private static readonly Dictionary<ConstructorInfo, DynamicMethod> dynamicMethods = new Dictionary<ConstructorInfo, DynamicMethod>();
		private static readonly MethodInfo readOnlyCollectionGetItem = GetReadOnlyCollectionGetItemMethod();

		private static MethodInfo GetReadOnlyCollectionGetItemMethod() {
			MethodInfo result = typeof(ReadOnlyCollection<SemanticToken>).GetProperty("Item").GetGetMethod();
			Debug.Assert(result != null);
			return result;
		}

		private static DynamicMethod GetDynamicMethod(ConstructorInfo constructor) {
			Debug.Assert(constructor != null);
			lock (dynamicMethods) {
				DynamicMethod result;
				if (!dynamicMethods.TryGetValue(constructor, out result)) {
					result = new DynamicMethod(string.Format("SemanticNonterminalTypeFactory<{0}>.Activator", constructor.DeclaringType.FullName), constructor.DeclaringType, new[] {typeof(int[]), typeof(ReadOnlyCollection<SemanticToken>)}, true);
					ILGenerator il = result.GetILGenerator();
					Dictionary<int, ParameterInfo> parameters = new Dictionary<int, ParameterInfo>();
					foreach (ParameterInfo parameter in constructor.GetParameters()) {
						parameters.Add(parameter.Position, parameter);
					}
					for (int i = 0; i < parameters.Count; i++) {
						if (parameters[i].ParameterType.IsValueType) {
							throw new InvalidOperationException("Constructor arguments cannot be value types");
						}
						Label loadNull = il.DefineLabel();
						Label end = il.DefineLabel();
						il.Emit(OpCodes.Ldarg_1); // load the ReadOnlyCollection<SemanticToken>
						il.Emit(OpCodes.Ldarg_0); // load the int[]
						il.Emit(OpCodes.Ldc_I4, i); // load the parameter index
						il.Emit(OpCodes.Ldelem_I4); // get the indirection index
						il.Emit(OpCodes.Dup); // copy the indicrection index
						il.Emit(OpCodes.Ldc_I4_M1); // and load a -1
						il.Emit(OpCodes.Beq_S, loadNull); // compare the stored indicrection index and the stored -1, if equal we need to load a null
						il.Emit(OpCodes.Callvirt, readOnlyCollectionGetItem); // otherwise get the item
						il.Emit(OpCodes.Castclass, parameters[i].ParameterType); // make the verifier happy by casting the reference
						il.Emit(OpCodes.Br_S, end); // jump to end
						il.MarkLabel(loadNull);
						il.Emit(OpCodes.Pop); // pop the unused indirection index
						il.Emit(OpCodes.Pop); // pop the unused reference to the ReadOnlyCollection<SemanticToken>
						il.Emit(OpCodes.Ldnull); // load a null reference instead
						il.MarkLabel(end);
					}
					il.Emit(OpCodes.Newobj, constructor); // invoke constructor
					il.Emit(OpCodes.Ret);
					dynamicMethods.Add(constructor, result);
				}
				return result;
			}
		}

		public static Activator<T> CreateActivator<T>(SemanticNonterminalTypeFactory<T> target, ConstructorInfo constructor, int[] parameterMapping) where T: SemanticToken {
			if (target == null) {
				throw new ArgumentNullException("target");
			}
			if (constructor == null) {
				throw new ArgumentNullException("constructor");
			}
			return (Activator<T>)GetDynamicMethod(constructor).CreateDelegate(typeof(Activator<T>), parameterMapping);
		}
	}
}