﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MongoFramework.Core
{
	public enum EntityState
	{
		/// <summary>
		/// There are no known changes to the entity
		/// </summary>
		NoChanges,
		/// <summary>
		/// The entity is known to be added during the next save
		/// </summary>
		Added,
		/// <summary>
		/// The entity is known to be updated during the next save
		/// </summary>
		Updated,
		/// <summary>
		/// The entity is known to be deleted during the next save
		/// </summary>
		Deleted
	}
}
