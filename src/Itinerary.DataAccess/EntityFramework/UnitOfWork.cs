﻿using System;
using Itinerary.DataAccess.Abstract.Repository;
using Itinerary.DataAccess.Abstract.UnitOfWork;
using Itinerary.DataAccess.EntityFramework.Repository;

namespace Itinerary.DataAccess.EntityFramework
{
  public class UnitOfWork : IUnitOfWork
  {
    private ItineraryDbContext _dbContext;
    private bool _isDisposed;

    public UnitOfWork( ItineraryDbContext dbContext )
    {
      _dbContext = dbContext;
      PlacesRepository = new PlacesRepository( dbContext );
      PlaceCategoriesRepository = new PlaceCategoriesRepository( dbContext );
    }

    public IPlacesRepository PlacesRepository { get; }

    public IPlaceCategoriesRepository PlaceCategoriesRepository { get; }

    public int SaveChanges()
    {
      CheckDisposed();
      return _dbContext.SaveChanges();
    }

    public void Dispose()
    {
      Dispose( true );
      GC.SuppressFinalize( this );
    }

    private void CheckDisposed()
    {
      if ( _isDisposed )
        throw new ObjectDisposedException( "The UnitOfWork is already disposed and cannot be used anymore." );
    }

    private void Dispose( bool disposing )
    {
      if ( !_isDisposed )
      {
        if ( disposing )
        {
          if ( _dbContext != null )
          {
            _dbContext.Dispose();
            _dbContext = null;
          }
        }
      }
      _isDisposed = true;
    }

    ~UnitOfWork()
    {
      Dispose( false );
    }
  }
}