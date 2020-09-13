using System;
using System.Collections.Generic;

namespace Terraria.ModLoader
{
	public class Loader<T>
	{
		//private readonly IList<T> instances = new List<T>();
		//public static IEnumerable<T> GetInstances()

		//public abstract void Clear();
	}

	public abstract class TypeLoader<T>:Loader<T> where T:class
	{
		protected static readonly List<T> instances = new List<T>();

		protected static int idOffset;

		public static int Count => instances.Count+idOffset;

		/// <summary>Good for checking if instance is bad or if an id limit has been hit and then throwing exceptions.</summary>
		protected internal delegate void RegistrationValidator(int id, T instance);
		protected internal static event RegistrationValidator ValidateRegistration;

		protected delegate void OnAddEvent(int id, T instance);
		protected static event OnAddEvent OnAdd;

		///// <summary></summary>
		///// <param name="idOffset">
		///// New IDs will start from this value (inclusively).
		///// Useful for when working with vanilla Id.
		///// </param>
		//public TypeLoader(int idOffset = 0) {
		//	this.idOffset = idOffset;
		//}

		public static void Clear() => instances.Clear();
		public static IEnumerable<T> GetInstances() => instances;

		public static int Register(T instance) {
			int id = idOffset+instances.Count;
			ValidateRegistration(id, instance);
			instances.Add(instance);
			OnAdd(id, instance);
			return id;
		}

		public static T Get(int type) {
			type -= idOffset;
			if(0<type && type<instances.Count){
				return instances[type];
			}
			return null;
		}

		protected static readonly RegistrationValidator RequireModdedClients = (id,instance) => {
			if (ModNet.AllowVanillaClients) throw new Exception("Adding mod types break vanilla client compatibility");
		};
	}

	public abstract class ModTypeLoader<T> : TypeLoader<T> where T:class,IModType
	{
		static ModTypeLoader() {
			ValidateRegistration += RequireModdedClients;
		}
	}

	//https://github.com/tModLoader/tModLoader/issues/674
	public abstract class ModTypeWithGlobalLoader<T,G> where T:class,IModType where G:class,IModType
	{
		static ModTypeWithGlobalLoader() {
			
		}

		protected abstract class InstanceLoader:TypeLoader<T>
		{

		}

		protected abstract class GlobalLoader:TypeLoader<G>
		{

		}
	}

	//public interface ILoader<out T>
	//{
	//	public abstract IEnumerable<T> GetInstances();

	//	public abstract void Clear();
	//}

	//public interface ITypeLoader<T> : ILoader<T>
	//{
	//	public abstract int Register(T instance);
	//	public abstract T Get(int id);
	//}

	//public class TypeLoader<T> : ITypeLoader<T> where T:class
	//{
	//	private readonly IList<T> instances = new List<T>();
	//	private readonly int idOffset;

	//	/// <summary></summary>
	//	/// <param name="idOffset">
	//	/// New IDs will start from this value (inclusively).
	//	/// Useful for when working with vanilla Id.
	//	/// </param>
	//	public TypeLoader(int idOffset = 0) {
	//		this.idOffset = idOffset;
	//	}

	//	public void Clear() => instances.Clear();
	//	public IEnumerable<T> GetInstances() => instances;

	//	public int Register(T instance) {
	//		int id = idOffset+instances.Count;
	//		ValidateRegistration(id, instance);
	//		instances.Add(instance);
	//		return id;
	//	}

	//	/// <summary>Good for checking if instance is bad or if an id limit has been hit and then throwing exceptions.</summary>
	//	protected virtual void ValidateRegistration(int id, T instance) { }

	//	//public virtual int ReserveID() => 

	//	public T Get(int type) {
	//		type -= idOffset;
	//		if(0<type && type<instances.Count){
	//			return instances[type];
	//		}
	//		return null;
	//	}
	//}

	//public class ModTypeLoader<T> : ITypeLoader<T> where T:class,IModType
	//{
	//	private readonly IList<T> instances = new List<T>();
	//	private readonly int vanillaCount;

	//	public int Count{get;private set;}

	//	/// <summary></summary>
	//	/// <param name="idOffset">
	//	/// New IDs will start from this value (inclusively).
	//	/// Useful for when working with vanilla Id.
	//	/// </param>
	//	public ModTypeLoader(int vanillaCount) {
	//		this.vanillaCount = vanillaCount;
	//		Count = vanillaCount;
	//	}

	//	public void Clear()
	//	{
	//		instances.Clear();
	//		Count = vanillaCount;
	//	}
	//	public IEnumerable<T> GetInstances() => instances;

	//	public int Register(T instance) {
	//		int id = ReserveID();
	//		Add(id, instance);
	//		instances.Add(instance);
	//		return id;
	//	}

	//	protected virtual void Add(int id, T instance) {}

	//	public virtual int ReserveID() {
	//		if (ModNet.AllowVanillaClients) throw new Exception("Adding mod types break vanilla client compatibility");
	//		return Count++;
	//	}

	//	public T Get(int type) {
	//		if(vanillaCount<type && type<Count){
	//			return instances[type - vanillaCount];
	//		}
	//		return null;
	//	}
	//}
}
