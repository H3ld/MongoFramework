﻿using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.IdGenerators;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MongoFramework.Infrastructure.Mutators;
using MongoFramework.Attributes;

namespace MongoFramework.Infrastructure
{
	public class DbEntityMapper<TEntity> : IDbEntityMapper<TEntity>
	{
		public BsonClassMap<TEntity> ClassMap { get; private set; }

		private HashSet<Type> configuredTypesCache { get; set; } = new HashSet<Type>();

		public DbEntityMapper()
		{
			InitialiseClassMap();
		}
		public DbEntityMapper(HashSet<Type> configuredTypesCache)
		{
			InitialiseClassMap();
			this.configuredTypesCache = configuredTypesCache;
		}
		
		public DbEntityMapper(BsonClassMap<TEntity> classMap)
		{
			ClassMap = classMap;
		}

		private void InitialiseClassMap()
		{
			//For reasons unknown to me, you can't just call "BsonClassMap.LookupClassMap" as that "freezes" the class map
			//Instead, you must do the lookup and initial creation yourself.
			var classMaps = BsonClassMap.GetRegisteredClassMaps();
			ClassMap = classMaps.Where(cm => cm.ClassType == typeof(TEntity)).FirstOrDefault() as BsonClassMap<TEntity>;

			if (ClassMap == null)
			{
				ClassMap = new BsonClassMap<TEntity>();
				ClassMap.AutoMap();
				BsonClassMap.RegisterClassMap(ClassMap);
				ConfigureEntity();
			}
		}

		/// <summary>
		/// Special handling is required for mapping base class fields (https://jira.mongodb.org/browse/CSHARP-398).
		/// Properties with a "Class" property type also need particular handling
		/// </summary>
		/// <param name="type"></param>
		private void ConfigureType(Type type)
		{
			if (configuredTypesCache.Contains(type))
			{
				return;
			}

			configuredTypesCache.Add(type);
			var entityWorkflowType = typeof(DbEntityMapper<>).MakeGenericType(type);
			var entityWorkflow = Activator.CreateInstance(entityWorkflowType, configuredTypesCache) as IDbEntityDescriptor;
			entityWorkflow.ConfigureEntity();
		}

		/// <summary>
		/// Configures various aspects of the entity including the Id, mapped fields and extra fields.
		/// </summary>
		public void ConfigureEntity()
		{
			if (ClassMap.IsFrozen)
			{
				return;
			}

			configuredTypesCache.Add(typeof(TEntity));

			ConfigureEntityId();
			ConfigureMappedFields();
			ConfigureExtraElements();
			ConfigureSubProperties();
		}

		/// <summary>
		/// If no Id is automatically found, finds the first property with <see cref="KeyAttribute"/> and uses that property as the Id.
		/// With any Id found or not, assigns a <see cref="IIdGenerator"/> to it appropriately based on the Id member's type.
		/// </summary>
		private void ConfigureEntityId()
		{
			if (ClassMap.IsFrozen)
			{
				return;
			}

			//If no Id member map exists, find the first property with the "Key" attribute and use that
			if (ClassMap.IdMemberMap == null)
			{
				var publicProperties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
				var idProperty = publicProperties.Where(p => p.GetCustomAttribute<KeyAttribute>() != null).FirstOrDefault();
				if (idProperty != null)
				{
					if (idProperty.DeclaringType == typeof(TEntity))
					{
						ClassMap.MapIdField(idProperty.Name);
					}
					else
					{
						ConfigureType(idProperty.DeclaringType);
					}
				}
			}

			//If there is no Id generator, set a default based on the member type
			if (ClassMap.IdMemberMap != null && ClassMap.IdMemberMap.IdGenerator == null)
			{
				var idMemberMap = ClassMap.IdMemberMap;
				var memberType = BsonClassMap.GetMemberInfoType(idMemberMap.MemberInfo);
				if (memberType == typeof(string))
				{
					idMemberMap.SetIdGenerator(StringObjectIdGenerator.Instance);
				}
				else if (memberType == typeof(Guid))
				{
					idMemberMap.SetIdGenerator(CombGuidGenerator.Instance);
				}
				else if (memberType == typeof(ObjectId))
				{
					idMemberMap.SetIdGenerator(ObjectIdGenerator.Instance);
				}
			}
		}

		/// <summary>
		/// Finds all properties marked with <see cref="NotMappedAttribute"/> and unmaps the properties in the <see cref="BsonClassMap"/>
		/// </summary>
		private void ConfigureMappedFields()
		{
			if (ClassMap.IsFrozen)
			{
				return;
			}

			var publicProperties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);

			foreach (var property in publicProperties)
			{
				if (property.DeclaringType == typeof(TEntity))
				{
					//Unmap fields with the "NotMappedAttribute"
					var notMappedAttribute = property.GetCustomAttribute<NotMappedAttribute>();
					if (notMappedAttribute != null)
					{
						ClassMap.UnmapProperty(property.Name);
						continue;
					}

					//Remap fields with the "ColumnAttribute"
					var columnAttribute = property.GetCustomAttribute<ColumnAttribute>();
					if (columnAttribute != null)
					{
						var mappedName = columnAttribute.Name;
						var memberMap = ClassMap.GetMemberMap(property.Name);
						memberMap.SetElementName(mappedName);
					}
				}
				else
				{
					ConfigureType(property.DeclaringType);
				}
			}
		}

		/// <summary>
		/// Checks to see if <see cref="IgnoreExtraElementsAttribute"/> is set on <see cref="TEntity"/> and applies it if it is.
		/// Otherwise finds the first property named "ExtraElements" with the type <see cref="IDictionary{object, object}"/>, using that as a catch-all when not all properties match.
		/// </summary>
		private void ConfigureExtraElements()
		{
			if (ClassMap.IsFrozen)
			{
				return;
			}

			var ignoreExtraElementsAttribute = typeof(TEntity).GetCustomAttribute<IgnoreExtraElementsAttribute>();
			if (ignoreExtraElementsAttribute != null)
			{
				ClassMap.SetIgnoreExtraElements(true);
				ClassMap.SetIgnoreExtraElementsIsInherited(ignoreExtraElementsAttribute.IgnoreInherited);
			}
			else
			{
				var publicProperties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
				var extraElementsProperty = publicProperties.Where(p => p.Name.ToLower() == "extraelements").FirstOrDefault();
				var extraElementsRequiredType = typeof(IDictionary<object, object>);

				if (extraElementsProperty != null && extraElementsRequiredType.IsAssignableFrom(extraElementsProperty.PropertyType))
				{
					ClassMap.SetExtraElementsMember(new BsonMemberMap(ClassMap, extraElementsProperty));
				}
			}
		}

		/// <summary>
		/// Checks all public properties on <see cref="TEntity"/> for any classes that may also need configuring.
		/// </summary>
		private void ConfigureSubProperties()
		{
			if (ClassMap.IsFrozen)
			{
				return;
			}

			var publicProperties = typeof(TEntity).GetProperties(BindingFlags.Public | BindingFlags.Instance);
			var propertiesWithClasses = publicProperties.Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(TEntity));
			var mappedMembers = ClassMap.AllMemberMaps;

			foreach (var property in propertiesWithClasses)
			{
				//Check that the property is mapped. If so, we don't need to configure the property.
				if (mappedMembers.Any(m => m.MemberName == property.Name))
				{
					continue;
				}

				ConfigureType(property.PropertyType);
			}
		}

		public string GetCollectionName()
		{
			var tableAttribute = typeof(TEntity).GetCustomAttribute<TableAttribute>();
			if (tableAttribute != null)
			{
				if (string.IsNullOrEmpty(tableAttribute.Schema))
				{
					return tableAttribute.Name;
				}
				else
				{
					return tableAttribute.Schema + "." + tableAttribute.Name;
				}
			}
			else
			{
				return typeof(TEntity).Name;
			}
		}

		public string GetEntityIdName()
		{
			var idMemberMap = ClassMap.IdMemberMap;
			if (idMemberMap != null)
			{
				return idMemberMap.MemberName;
			}
			return null;
		}

		public object GetEntityIdValue(TEntity entity)
		{
			var idMemberMap = ClassMap.IdMemberMap;
			if (idMemberMap != null)
			{
				if (idMemberMap.MemberInfo is PropertyInfo propertyInfo)
				{
					return propertyInfo.GetValue(entity);
				}
			}
			return null;
		}

		public IEnumerable<PropertyInfo> GetMappedProperties()
		{
			return ClassMap.DeclaredMemberMaps.Select(m => m.MemberInfo as PropertyInfo);
		}
	}
}
