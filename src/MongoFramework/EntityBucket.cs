﻿using System;
using System.Collections.Generic;
using System.Text;

namespace MongoFramework
{
	public class EntityBucket<TGroup, TSubEntity> where TGroup : class
	{
		public string Id { get; set; }
		public TGroup Group { get; set; }
		public int Index { get; set; }
		public int ItemCount { get; set; }
		public int BucketSize { get; set; }
		public TSubEntity[] Items { get; set; }
	}
}
