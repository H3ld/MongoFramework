﻿using MongoDB.Driver;
using MongoFramework.Infrastructure.Mapping;
using MongoFramework.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MongoFramework.Infrastructure.EntityRelationships
{
	public class EntityNavigationCollection<TEntity> : EntityCollection<TEntity>, IEntityNavigationCollection<TEntity> where TEntity : class
	{
		private IMongoDbConnection Connection { get; set; }
		private IEntityMapper EntityMapper { get; }
		private IEntityPropertyMap ForeignPropertyMap { get; }
		private HashSet<object> UnloadedIds { get; } = new HashSet<object>();

		public string ForeignKey { get; }

		public new int Count => LoadedCount + UnloadedCount;

		public int LoadedCount => Entries.Count;
		public int UnloadedCount => UnloadedIds.Count;
		
		public EntityNavigationCollection(string foreignKey, IMongoDbConnection connection) : base(connection?.GetEntityMapper(typeof(TEntity)))
		{
			ForeignKey = foreignKey ?? throw new ArgumentNullException(nameof(foreignKey));
			Connection = connection ?? throw new ArgumentNullException(nameof(connection));
			EntityMapper = connection.GetEntityMapper(typeof(TEntity));
			ForeignPropertyMap = EntityMapper.GetEntityMapping().Where(m => m.Property.Name == foreignKey).FirstOrDefault();
		}
		
		public void AddForeignId(object foreignId)
		{
			if (foreignId == null)
			{
				throw new ArgumentNullException(nameof(foreignId));
			}

			//Check the EntityId matches the known type for TEntity
			if (!ForeignPropertyMap.PropertyType.Equals(foreignId.GetType()))
			{
				throw new InvalidOperationException($"Type mismatch for foreign key. {foreignId.GetType()} specified however expected type {ForeignPropertyMap.PropertyType}");
			}

			//Check the entity isn't already loaded
			var foreignProperty = ForeignPropertyMap.Property;
			if (!Entries.Any(e => Equals(foreignId, foreignProperty.GetValue(e.Entity))))
			{
				UnloadedIds.Add(foreignId);
			}
		}

		public void AddForeignIds(IEnumerable<object> foreignIds)
		{
			foreach (var foreignId in foreignIds)
			{
				AddForeignId(foreignId);
			}
		}

		public void LoadEntities()
		{
			if (!UnloadedIds.Any())
			{
				return;
			}

			var dbEntityReader = new EntityReader<TEntity>(Connection);
			var entities = dbEntityReader.AsQueryable().WherePropertyMatches(ForeignKey, ForeignPropertyMap.PropertyType, UnloadedIds);

			foreach (var entity in entities)
			{
				Update(entity, EntityEntryState.NoChanges);
			}

			UnloadedIds.Clear();
		}

		public IEnumerable<object> GetForeignIds()
		{
			var loadedEntityIds = Entries.Select(e => ForeignPropertyMap.Property.GetValue(e.Entity));
			return loadedEntityIds.Concat(UnloadedIds);
		}

		public new IEnumerator<TEntity> GetEnumerator()
		{
			//Enumerate loaded entities
			var result = Entries.Select(e => e.Entity);
			using (var enumerator = result.GetEnumerator())
			{
				while (enumerator.MoveNext())
				{
					yield return enumerator.Current;
				}
			}

			//Enumerate list of unloaded IDs and load them in one at a time
			if (UnloadedIds.Any())
			{
				var dbEntityReader = new EntityReader<TEntity>(Connection);
				var unloadedEntities = dbEntityReader.AsQueryable().WherePropertyMatches(ForeignKey, ForeignPropertyMap.PropertyType, UnloadedIds);

				using (var unloadedEnumerator = unloadedEntities.GetEnumerator())
				{
					while (unloadedEnumerator.MoveNext())
					{
						var loadedEntity = unloadedEnumerator.Current;

						//Load the data into the collection so we don't need to query it again
						Update(loadedEntity, EntityEntryState.NoChanges);

						//Remove from unloaded entity collection
						var foreignId = ForeignPropertyMap.Property.GetValue(loadedEntity);
						UnloadedIds.Remove(foreignId);

						yield return loadedEntity;
					}
				}
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
