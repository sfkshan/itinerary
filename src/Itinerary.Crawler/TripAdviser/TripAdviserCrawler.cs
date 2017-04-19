﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using Itinerary.Crawler.TripAdviser.Entities;
using Itinerary.DataAccess.Abstract.UnitOfWork;
using Itinerary.DataAccess.Entities;
using Itinerary.DataAccess.EntityFramework;
using LiteDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Itinerary.Crawler.TripAdviser
{
  internal class TripAdviserCrawler : IDisposable
  {
    private readonly TimeSpan _delay;
    private readonly ILogger _logger;
    private readonly LiteDatabase _liteDatabase;
    private readonly HashSet<Attraction> _attractions = new HashSet<Attraction>();

    private static readonly Regex CategoryRegex = new Regex(
      pattern: "<div class=\"gray-footer \">(?<category>.*?)<\\/div>", options: RegexOptions.Singleline );

    private static readonly Regex ImgRegex =
      new Regex( pattern: "https://media-cdn.tripadvisor.com/media/photo-[^']+.jpg" );

    private static readonly Regex RatingRegex =
      new Regex( pattern: "(?<rating>[0-9,.]+) of 5 bubbles" );

    private static readonly Regex ReviewsRegex = new Regex( pattern: "(?<reviews>[0-9,.]+) review" );

    public TripAdviserCrawler( string outputFile, TimeSpan delay, ILogger logger )
    {
      _delay = delay;
      _logger = logger;
      _liteDatabase = new LiteDatabase( outputFile );
    }

    public void ConvertToWebDb( string outputFile )
    {
      DbContextOptions<ItineraryDbContext> options = new DbContextOptionsBuilder<ItineraryDbContext>()
        .UseSqlite( $"Data Source={outputFile}" )
        .Options;

      using ( var unitOfWork = new UnitOfWork( new ItineraryDbContext( options ) ) )
      {
        var places = new List<Place>();
        List<Segment> segments = GetSegmentsCollection().FindAll().ToList();
        var categories = new List<string>();

        foreach ( Segment segment in segments )
        {
          IList<Attraction> attractions = segment.Map.Attractions;
          foreach ( Attraction attraction in attractions )
          {
            categories.AddRange( attraction.Categories ?? Enumerable.Empty<string>() );
          }
        }
        Dictionary<string, PlaceCategory> createdCategories = CreateCategories( unitOfWork, categories.Distinct() );

        foreach ( Segment segment in segments )
        {
          if ( !segment.Map.Attractions.Any() ) continue;

          IEnumerable<Place> attractionPlaces =
            from attraction in segment.Map.Attractions
            let place = new Place
                        {
                          Name = attraction.CustomHover.Title,
                          Rating = attraction.Rating,
                          Reviews = attraction.Reviews,
                          Categories = GetPlaceCategories( createdCategories, attraction.Categories ),
                          ImgUrl = attraction.ImgUrl,
                          Url = attraction.Url,
                          Latitude = attraction.Lat,
                          Longitude = attraction.Lng
                        }
            select place;

          places.AddRange( attractionPlaces );
        }

        _logger.LogInformation( $"Total {places.Count} places found." );

        places = places.Distinct( new PlaceEqualityComparer() ).ToList();

        _logger.LogInformation( $"Total {places.Count} places after cleanup." );

        foreach ( Place place in places )
        {
          unitOfWork.PlacesRepository.Insert( place );
        }

        unitOfWork.SaveChanges();
      }
    }

    public void Run( double startLat, double startLng, double endLat, double endLng, double zoom, double size )
    {
      int index = 0;

      _logger.LogInformation( "Removing bad data..." );

      BsonValue[] badRecords =
        GetSegmentsCollection()
          .FindAll()
          .Where(
            x =>
              x.Map.Attractions.Any(
                a => a.Rating == 0 || a.Reviews == 0 || string.IsNullOrEmpty( a.ImgUrl ) ) )
          .Select( x => new BsonValue( x.Id ) )
          .ToArray();
      GetSegmentsCollection().Delete( Query.In( "Id", badRecords ) );

      _logger.LogInformation( $"Removed {badRecords.Length} records with not filled rating, reviews etc." );

      _logger.LogInformation( "Reading existing data..." );

      Dictionary<string, Segment> segments = GetSegmentsCollection()
        .Find( x => x.Zoom == zoom && x.Size == size )
        .ToDictionary( x => GetKey( x.Latitude, x.Longitude ), x => x );

      _logger.LogInformation( "Preparing points..." );

      var coordinates = new List<(double lat, double lng)>();
      for ( double lat = endLat; lat <= startLat; lat += zoom * 10 / size )
      for ( double lng = startLng; lng <= endLng; lng += zoom * 10 / size )
      {
        if ( !segments.ContainsKey( GetKey( lat, lng ) ) )
          coordinates.Add( (lat: lat, lng: lng) );
      }

      foreach ( (double lat, double lng) coordinate in coordinates )
      {
        double lat = coordinate.lat;
        double lng = coordinate.lng;

        ReadSegment( lat, lng, zoom, size );

        Interlocked.Increment( ref index );
        _logger.LogInformation( $"{index * 100.0 / coordinates.Count} complete" );
      }
    }

    private static string GetKey( double lat, double lng )
    {
      return $"{lat}{lng}";
    }

    private LiteCollection<Segment> GetSegmentsCollection()
    {
      return _liteDatabase.GetCollection<Segment>( "segments" );
    }

    private void ReadSegment( double lat, double lng, double zoom, double size )
    {
      try
      {
        var httpClient = new HttpClient();
        string url =
          $"https://www.tripadvisor.com/GMapsLocationController?Action=update&from=Attractions&g=35805&geo=35805&mapProviderFeature=ta-maps-gmaps3&validDates=false&mc={lat},{lng}&mz={zoom}&mw={size}&mh={size}&pinSel=v2&origLocId=35805&sponsors=&finalRequest=false&includeMeta=false&trackPageView=false";

        Thread.Sleep( TimeSpan.FromSeconds( 0.1 ) );
        byte[] json = httpClient.GetByteArrayAsync( url ).Result;

        _logger.LogDebug( $"Read segment {lat}, {lng}. {json.Length} bytes." );

        using ( var streamReader = new StreamReader( new MemoryStream( json ) ) )
        using ( var jsonTextReader = new JsonTextReader( streamReader ) )
        {
          var serializer = new Newtonsoft.Json.JsonSerializer();
          var map = serializer.Deserialize<Map>( jsonTextReader );

          var segment = new Segment() { Latitude = lat, Longitude = lng, Zoom = zoom, Size = size, Map = map };
          _logger.LogInformation( $"Found {map.Attractions.Count} attractions" );

          GetSegmentsCollection().Insert( segment );

          foreach ( Attraction attraction in segment.Map.Attractions )
          {
            _logger.LogDebug( $"Attraction: {attraction.CustomHover.Title}" );

            string infoUrl =
              $"https://www.tripadvisor.com{attraction.CustomHover.Url.Replace( "Action=info", "Action=infoCardAttr" )}";

            Thread.Sleep( _delay );

            string html = httpClient.GetStringAsync( infoUrl ).Result;
            string imgUrl = ImgRegex.Match( html ).Value;
            string reviews = ReviewsRegex.Match( html ).Groups[ groupname: "reviews" ].Value;
            string rating = RatingRegex.Match( html ).Groups[ groupname: "rating" ].Value;
            string categories = CategoryRegex
              .Match( html )
              .Groups[ groupname: "category" ]
              .Value.Trim( '\r', '\n', ' ' );

            _logger.LogDebug( $"reviews: {reviews}, rating: {rating}, categories: {categories}, image: {imgUrl}" );

            attraction.Categories =
              ( categories ?? string.Empty )
              .Split( separator: new[] { ',' }, options: StringSplitOptions.RemoveEmptyEntries )
              .Select( c => c.Trim( ' ' ) )
              .ToArray();

            attraction.ImgUrl = imgUrl;

            if ( !string.IsNullOrEmpty( reviews ) )
              attraction.Reviews = int.Parse( reviews, NumberStyles.AllowThousands );

            if ( !string.IsNullOrEmpty( rating ) )
              attraction.Rating = float.Parse( rating );

            if ( !_attractions.Contains( attraction ) )
              _attractions.Add( attraction );
            else
              _logger.LogDebug( $"Attraction {attraction.CustomHover.Title} already exists." );
          }

          GetSegmentsCollection().Update( segment );
        }
      }
      catch ( Exception e )
      {
        _logger.LogError( e.Message );
      }
    }

    private static Dictionary<string, PlaceCategory> CreateCategories(
      IUnitOfWork unitOfWork,
      IEnumerable<string> categories )
    {
      foreach ( string category in categories )
      {
        unitOfWork.PlaceCategoriesRepository.Insert( new PlaceCategory { Name = category } );
      }

      unitOfWork.SaveChanges();
      return unitOfWork.PlaceCategoriesRepository.Get( _ => true ).ToDictionary( x => x.Name, x => x );
    }

    private static ICollection<PlacePlaceCategory> GetPlaceCategories(
      IReadOnlyDictionary<string, PlaceCategory> existingCategories,
      IEnumerable<string> categories )
    {
      return categories != null
               ? categories
                 .Select( category => existingCategories[ category ] )
                 .Select( placeCategory => new PlacePlaceCategory { Category = placeCategory } )
                 .ToList()
               : new List<PlacePlaceCategory>();
    }

    public void Dispose()
    {
      _liteDatabase.Dispose();
    }
  }

  internal class PlaceEqualityComparer : IEqualityComparer<Place>
  {
    public bool Equals( Place x, Place y )
    {
      if ( ReferenceEquals( x, y ) ) return true;
      if ( ReferenceEquals( x, null ) ) return false;
      if ( ReferenceEquals( y, null ) ) return false;
      if ( x.GetType() != y.GetType() ) return false;

      return string.Equals( x.Name, y.Name ) &&
             x.Rating == y.Rating &&
             x.Reviews == y.Reviews &&
             x.Latitude == y.Latitude &&
             x.Longitude == y.Longitude;
    }

    public int GetHashCode( Place obj )
    {
      unchecked
      {
        int result = obj.Name.GetHashCode();
        result = ( result * 397 ) ^ obj.Rating.GetHashCode();
        result = ( result * 397 ) ^ obj.Reviews.GetHashCode();
        result = ( result * 397 ) ^ obj.Latitude.GetHashCode();
        result = ( result * 397 ) ^ obj.Longitude.GetHashCode();
        return result;
      }
    }
  }
}
