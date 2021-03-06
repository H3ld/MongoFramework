﻿using Microsoft.VisualStudio.TestTools.UnitTesting;
using MongoDB.Bson.Serialization;
using MongoFramework.Infrastructure.Mapping;
using MongoFramework.Infrastructure.Mapping.Processors;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace MongoFramework.Tests.Infrastructure.Mapping.Processors
{
	[TestClass]
	public class MappedPropertiesProcessorTests : TestBase
	{
		public class ColumnAttributePropertyModel
		{
			public string Id { get; set; }
			[Column("CustomPropertyName")]
			public string MyProperty { get; set; }
		}

		public class NotMappedPropertiesModel
		{
			public string Id { get; set; }
			[NotMapped]
			public string NotMapped { get; set; }
		}

		[TestMethod]
		public void ObeysNotMappedAttribute()
		{
			var connection = TestConfiguration.GetConnection();
			var processor = new MappedPropertiesProcessor();
			var classMap = new BsonClassMap<NotMappedPropertiesModel>();
			classMap.AutoMap();
			processor.ApplyMapping(typeof(NotMappedPropertiesModel), classMap, connection);

			var entityMapper = connection.GetEntityMapper(typeof(NotMappedPropertiesModel));
			var mappedProperties = entityMapper.GetEntityMapping();
			Assert.IsFalse(mappedProperties.Any(p => p.ElementName == "NotMapped"));
		}

		[TestMethod]
		public void ObeysColumnAttributeRemap()
		{
			var connection = TestConfiguration.GetConnection();
			var processor = new MappedPropertiesProcessor();
			var classMap = new BsonClassMap<ColumnAttributePropertyModel>();
			classMap.AutoMap();
			processor.ApplyMapping(typeof(ColumnAttributePropertyModel), classMap, connection);

			var entityMapper = connection.GetEntityMapper(typeof(ColumnAttributePropertyModel));
			var mappedProperties = entityMapper.GetEntityMapping();
			Assert.IsTrue(mappedProperties.Any(p => p.ElementName == "CustomPropertyName"));
		}
	}
}