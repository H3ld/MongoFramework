﻿using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Driver;
using System.Linq.Expressions;
using MongoFramework.Infrastructure;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using MongoDB.Driver.Linq;
using MongoFramework.Infrastructure.Linq;
using MongoFramework.Infrastructure.Linq.Processors;

namespace MongoFramework
{
	/// <summary>
	/// Basic Mongo "DbSet", providing changeset support and attribute validation
	/// </summary>
	/// <typeparam name="TEntity"></typeparam>
	public class MongoDbSet<TEntity> : IMongoDbSet<TEntity>
	{
		public IDbChangeTracker<TEntity> ChangeTracker { get; private set; } = new DbChangeTracker<TEntity>();

		private IDbEntityChangeWriter<TEntity> dbWriter { get; set; }
		private IDbEntityReader<TEntity> dbReader { get; set; }

		/// <summary>
		/// Whether any entity validation is performed prior to saving changes. (Default is true)
		/// </summary>
		public bool PerformEntityValidation { get; set; } = true;

		public MongoDbSet() { }

		/// <summary>
		/// Creates a new MongoDbSet using the connection string in the configuration that matches the specified connection name.
		/// </summary>
		/// <param name="connectionName">The name of the connection string stored in the configuration.</param>
		public MongoDbSet(string connectionName)
		{
			var mongoUrl = MongoDbUtility.GetMongoUrlFromConfig(connectionName);
			
			if (mongoUrl == null)
			{
				throw new MongoConfigurationException("No connection string found with the name \'" + connectionName + "\'");
			}

			SetDatabase(MongoDbUtility.GetDatabase(mongoUrl));
		}

		/// <summary>
		/// Creates a new MongoDbSet using the specified connection string and database combination.
		/// </summary>
		/// <param name="connectionString">The connection string to the server</param>
		/// <param name="databaseName">The database name on the server</param> 
		public MongoDbSet(string connectionString, string databaseName)
		{
			SetDatabase(MongoDbUtility.GetDatabase(connectionString, databaseName));
		}

		/// <summary>
		/// Creates a new MongoDbSet with the specified entity reader and writer.
		/// </summary>
		/// <param name="reader">The reader to use for querying the database.</param>
		/// <param name="writer">The writer to use for writing to the database.</param>
		public MongoDbSet(IDbEntityReader<TEntity> reader, IDbEntityChangeWriter<TEntity> writer)
		{
			dbReader = reader;
			dbWriter = writer;
		}
		
		/// <summary>
		/// Initialise a new entity reader and writer to the specified database.
		/// </summary>
		/// <param name="database"></param>
		public void SetDatabase(IMongoDatabase database)
		{
			var entityMapper = new DbEntityMapper<TEntity>();
			dbWriter = new DbEntityWriter<TEntity>(database, entityMapper);
			dbReader = new DbEntityReader<TEntity>(database, entityMapper);
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

			ChangeTracker.Update(entity, DbEntityEntryState.Added);
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
				Add(entity);
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

			ChangeTracker.Update(entity, DbEntityEntryState.Updated);
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
				Update(entity);
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

			ChangeTracker.Update(entity, DbEntityEntryState.Deleted);
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
				Remove(entity);
			}
		}

		/// <summary>
		/// Writes all of the items in the changeset to the database.
		/// </summary>
		public virtual void SaveChanges()
		{
			if (dbWriter == null)
			{
				throw new InvalidOperationException("No IDbEntityWriter has been set.");
			}

			ChangeTracker.DetectChanges();

			if (PerformEntityValidation)
			{
				var savingEntities = ChangeTracker.Entries()
					.Where(e => e.State == DbEntityEntryState.Added || e.State == DbEntityEntryState.Updated)
					.Select(e => e.Entity);

				foreach (var savingEntity in savingEntities)
				{
					var validationContext = new ValidationContext(savingEntity);
					Validator.ValidateObject(savingEntity, validationContext);
				}
			}

			dbWriter.WriteChanges(ChangeTracker);
		}

		#region IQueryable Implementation

		private IQueryable<TEntity> GetQueryable()
		{
			if (dbReader == null)
			{
				throw new InvalidOperationException("No IDbEntityReader has been set.");
			}

			var queryable = dbReader.AsQueryable() as IMongoFrameworkQueryable<TEntity, TEntity>;
			queryable.EntityProcessors.Add(new EntityTrackingProcessor<TEntity>(ChangeTracker));
			return queryable;
		}

		public Expression Expression
		{
			get
			{
				return GetQueryable().Expression;
			}
		}

		public Type ElementType
		{
			get
			{
				return GetQueryable().ElementType;
			}
		}

		public IQueryProvider Provider
		{
			get
			{
				return GetQueryable().Provider;
			}
		}

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