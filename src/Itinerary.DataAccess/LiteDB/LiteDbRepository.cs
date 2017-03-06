﻿using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Itinerary.Common.Entities;
using Itinerary.DataAccess.Interfaces;
using LiteDB;

namespace Itinerary.DataAccess.LiteDB
{
  public class LiteDbRepository<TEntity> : IRepository<TEntity>
    where TEntity : EntityBase
  {
    private readonly LiteCollection<TEntity> _collection;

    public LiteDbRepository( LiteDatabase database )
    {
      _collection = GetCollection( database );
    }

    public IEnumerable<TEntity> Get( Expression<Func<TEntity, bool>> predicate )
    {
      return _collection.Find( predicate );
    }

    public TEntity GetById( Guid id )
    {
      return _collection.FindOne( x => x.Id == id );
    }

    public TEntity Insert( TEntity entity )
    {
      entity.Id = _collection.Insert( entity ).AsGuid;
      return entity;
    }

    public void InsertMany( IEnumerable<TEntity> entities )
    {
      _collection.Insert( entities );
    }

    public TEntity Update( TEntity entity )
    {
      _collection.Update( entity );
      return entity;
    }

    public void UpdateMany( IEnumerable<TEntity> entities )
    {
      _collection.Update( entities );
    }

    public void Delete( Guid id )
    {
      _collection.Delete( x => x.Id == id );
    }

    public void Delete( TEntity entity )
    {
      Delete( entity.Id );
    }

    public void Delete( Expression<Func<TEntity, bool>> predicate )
    {
      _collection.Delete( predicate );
    }

    public long Count()
    {
      return _collection.Count();
    }

    public long Count( Expression<Func<TEntity, bool>> predicate )
    {
      return _collection.Count( predicate );
    }

    public bool Exists( Expression<Func<TEntity, bool>> predicate )
    {
      return _collection.Exists( predicate );
    }

    private LiteCollection<TEntity> GetCollection( LiteDatabase database )
    {
      return database.GetCollection<TEntity>( Utils.GetCollectionName<TEntity>() );
    }
  }
}