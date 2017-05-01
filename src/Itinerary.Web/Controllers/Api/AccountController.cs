﻿using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Itinerary.DataAccess.EntityFramework;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace Itinerary.Web.Controllers.Api
{
  public class RegisterViewModel
  {
    [Required]
    [EmailAddress]
    [Display( Name = "Email" )]
    public string Email { get; set; }

    [Required]
    [StringLength( 100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6 )]
    [DataType( DataType.Password )]
    [Display( Name = "Password" )]
    public string Password { get; set; }
  }

  [ApiVersion("1.0")]
  [Route( "api/v{version:apiVersion}/[controller]" )]
  [Authorize]
  public class AccountController : Controller
  {
    private readonly UserManager<IdentityUser> _userManager;
    private readonly ItineraryDbContext _itineraryDbContext;

    public AccountController(
      UserManager<IdentityUser> userManager,
      ItineraryDbContext itineraryDbContext )
    {
      _userManager = userManager;
      _itineraryDbContext = itineraryDbContext;
    }

    //
    // POST: /Account/Register
    [HttpPost( "[action]" )]
    [AllowAnonymous]
    public async Task<IActionResult> Register( [FromBody] RegisterViewModel model )
    {
      if ( ModelState.IsValid )
      {
        var user = new IdentityUser { UserName = model.Email, Email = model.Email };
        var result = await _userManager.CreateAsync( user, model.Password );
        if ( result.Succeeded )
        {
          return Ok();
        }
        AddErrors( result );
      }

      // If we got this far, something failed.
      return BadRequest( ModelState );
    }

    #region Helpers

    private void AddErrors( IdentityResult result )
    {
      foreach ( var error in result.Errors )
      {
        ModelState.AddModelError( string.Empty, error.Description );
      }
    }

    #endregion
  }
}
