﻿using MongoDB.Driver;
using MongoFramework.Infrastructure;
using MongoFramework.Infrastructure.EntityRelationships;
using MongoFramework.Infrastructure.Indexing;
using MongoFramework.Infrastructure.Linq;
using MongoFramework.Infrastructure.Linq.Processors;
using MongoFramework.Infrastructure.Mapping;
using MongoFramework.Infrastructure.Mutation;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace MongoFramework
{
	/// <summary>
	/// Basic Mongo "DbSet", providing changeset support and attribute validation
	/// </summary>
	/// <typeparam name="TEntity"></typeparam>
	public class MongoDbSet<TEntity> : IMongoDbSet<TEntity> where TEntity : class
	{
		public IEntityChangeTracker<TEntity> ChangeTracker { get; private set; }

		private IMongoDbConnection Connection { get; set; }
		private IEntityWriter<TEntity> EntityWriter { get; set; }
		private IEntityReader<TEntity> EntityReader { get; set; }
		private IEntityIndexWriter<TEntity> EntityIndexWriter { get; set; }
		private IEntityRelationshipWriter<TEntity> EntityRelationshipWriter { get; set; }

		/// <summary>
		/// Whether any entity validation is performed prior to saving changes. (Default is true)
		/// </summary>
		public bool PerformEntityValidation { get; set; } = true;

		public MongoDbSet() { }

		/// <summary>
		/// Initialise a new entity reader and writer to the specified database.
		/// </summary>
		/// <param name="connection"></param>
		public void SetConnection(IMongoDbConnection connection)
		{
			Connection = connection;
			EntityWriter = new EntityWriter<TEntity>(connection);
			EntityReader = new EntityReader<TEntity>(connection);
			EntityIndexWriter = new EntityIndexWriter<TEntity>(connection);
			EntityRelationshipWriter = new EntityRelationshipWriter<TEntity>(connection);
			ChangeTracker = new EntityChangeTracker<TEntity>(connection.GetEntityMapper(typeof(TEntity)));
		}

		public virtual TEntity Create()
		{
			var entity = Activator.CreateInstance<TEntity>();
			EntityMutation<TEntity>.MutateEntity(entity, MutatorType.Create, Connection);
			Add(entity);
			return entity;
		}

		/// <summary>
		/// Marks the entity for insertion into the database.
		/// </summary>
		/// <param name="entity"></param>
		public virtual void Add(TEntity entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException("entity");
			}

			ChangeTracker.Update(entity, EntityEntryState.Added);
		}
		/// <summary>
		/// Marks the collection of entities for insertion into the database.
		/// </summary>
		/// <param name="entities"></param>
		public virtual void AddRange(IEnumerable<TEntity> entities)
		{
			if (entities == null)
			{
				throw new ArgumentNullException("entities");
			}

			foreach (var entity in entities)
			{
				ChangeTracker.Update(entity, EntityEntryState.Added);
			}
		}

		/// <summary>
		/// Marks the entity for updating.
		/// </summary>
		/// <param name="entity"></param>
		public virtual void Update(TEntity entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException("entity");
			}

			ChangeTracker.Update(entity, EntityEntryState.Updated);
		}
		/// <summary>
		/// Marks the collection of entities for updating.
		/// </summary>
		/// <param name="entities"></param>
		public virtual void UpdateRange(IEnumerable<TEntity> entities)
		{
			if (entities == null)
			{
				throw new ArgumentNullException("entities");
			}

			foreach (var entity in entities)
			{
				ChangeTracker.Update(entity, EntityEntryState.Updated);
			}
		}

		/// <summary>
		/// Marks the entity for deletion.
		/// </summary>
		/// <param name="entity"></param>
		public virtual void Remove(TEntity entity)
		{
			if (entity == null)
			{
				throw new ArgumentNullException("entity");
			}

			ChangeTracker.Update(entity, EntityEntryState.Deleted);
		}
		/// <summary>
		/// Marks the collection of entities for deletion.
		/// </summary>
		/// <param name="entities"></param>
		public virtual void RemoveRange(IEnumerable<TEntity> entities)
		{
			if (entities == null)
			{
				throw new ArgumentNullException("entities");
			}

			foreach (var entity in entities)
			{
				ChangeTracker.Update(entity, EntityEntryState.Deleted);
			}
		}

		private void CheckEntityValidation()
		{
			if (PerformEntityValidation)
			{
				var savingEntities = ChangeTracker.GetEntries()
					.Where(e => e.State == EntityEntryState.Added || e.State == EntityEntryState.Updated)
					.Select(e => e.Entity);

				foreach (var savingEntity in savingEntities)
				{
					var validationContext = new ValidationContext(savingEntity);
					Validator.ValidateObject(savingEntity, validationContext);
				}
			}
		}

		/// <summary>
		/// Writes all of the items in the changeset to the database.
		/// </summary>
		/// <returns></returns>
		public virtual void SaveChanges()
		{
			EntityIndexWriter.ApplyIndexing();
			EntityRelationshipWriter.CommitEntityRelationships(ChangeTracker);
			ChangeTracker.DetectChanges();
			CheckEntityValidation();
			EntityWriter.Write(ChangeTracker);
			ChangeTracker.CommitChanges();
		}

		/// <summary>
		/// Writes all of the items in the changeset to the database.
		/// </summary>
		/// <returns></returns>
		public virtual async Task SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
		{
			await EntityIndexWriter.ApplyIndexingAsync(cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			await EntityRelationshipWriter.CommitEntityRelationshipsAsync(ChangeTracker, cancellationToken).ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
			ChangeTracker.DetectChanges();
			CheckEntityValidation();
			cancellationToken.ThrowIfCancellationRequested();
			await EntityWriter.WriteAsync(ChangeTracker, cancellationToken).ConfigureAwait(false);
			ChangeTracker.CommitChanges();
		}

		#region IQueryable Implementation

		private IQueryable<TEntity> GetQueryable()
		{
			var queryable = EntityReader.AsQueryable() as IMongoFrameworkQueryable<TEntity, TEntity>;
			queryable.EntityProcessors.Add(new EntityTrackingProcessor<TEntity>(ChangeTracker));
			return queryable;
		}

		public Expression Expression => GetQueryable().Expression;

		public Type ElementType => GetQueryable().ElementType;

		public IQueryProvider Provider => GetQueryable().Provider;

		public IEnumerator<TEntity> GetEnumerator()
		{
			return GetQueryable().GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		#endregion
	}
}