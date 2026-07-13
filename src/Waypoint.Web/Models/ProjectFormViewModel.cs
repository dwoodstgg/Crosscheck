using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Waypoint.Application.Projects;
using Waypoint.Domain.Entities;

namespace Waypoint.Web.Models;

public class ProjectFormViewModel
{
    [Required]
    [Display(Name = "Client")]
    public Guid? ClientId { get; set; }

    [Required]
    [Display(Name = "Project name")]
    public string? Name { get; set; }

    [Required]
    [Display(Name = "Code")]
    [RegularExpression(@"^[A-Za-z0-9\-]{2,20}$", ErrorMessage = "2–20 letters, digits, or dashes (e.g. GEO-014).")]
    public string? Code { get; set; }

    [Required]
    [Display(Name = "Project manager")]
    public Guid? ProjectManagerId { get; set; }

    [Display(Name = "Start date")]
    [DataType(DataType.Date)]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "End date")]
    [DataType(DataType.Date)]
    public DateOnly? EndDate { get; set; }

    // Billing overrides — blank inherits the client's default (design decision 18).
    [Display(Name = "Billing contact name")]
    public string? BillingContactName { get; set; }

    [EmailAddress]
    [Display(Name = "Billing contact email")]
    public string? BillingContactEmail { get; set; }

    [Range(0, 365)]
    [Display(Name = "Payment terms (days)")]
    public int? PaymentTermsDays { get; set; }

    [Display(Name = "Address line 1")]
    public string? AddressLine1 { get; set; }

    [Display(Name = "Address line 2")]
    public string? AddressLine2 { get; set; }

    [Display(Name = "City")]
    public string? City { get; set; }

    [Display(Name = "State")]
    public string? State { get; set; }

    [Display(Name = "Postal code")]
    public string? PostalCode { get; set; }

    public List<SelectListItem> ClientOptions { get; set; } = [];
    public List<SelectListItem> ManagerOptions { get; set; } = [];

    private BillingAddress? ToBillingAddress()
    {
        if (string.IsNullOrWhiteSpace(AddressLine1) && string.IsNullOrWhiteSpace(City)
            && string.IsNullOrWhiteSpace(State) && string.IsNullOrWhiteSpace(PostalCode))
        {
            return null;
        }

        return new BillingAddress
        {
            Line1 = AddressLine1?.Trim(),
            Line2 = string.IsNullOrWhiteSpace(AddressLine2) ? null : AddressLine2.Trim(),
            City = City?.Trim(),
            State = State?.Trim(),
            PostalCode = PostalCode?.Trim(),
        };
    }

    public ProjectBillingInput ToBillingInput() =>
        new(BillingContactName, BillingContactEmail, ToBillingAddress(), PaymentTermsDays);

    public static ProjectFormViewModel From(Project project) => new()
    {
        ClientId = project.ClientId,
        Name = project.Name,
        Code = project.Code,
        ProjectManagerId = project.ProjectManagerId,
        StartDate = project.StartDate,
        EndDate = project.EndDate,
        BillingContactName = project.BillingContactName,
        BillingContactEmail = project.BillingContactEmail,
        PaymentTermsDays = project.PaymentTermsDays,
        AddressLine1 = project.BillingAddress?.Line1,
        AddressLine2 = project.BillingAddress?.Line2,
        City = project.BillingAddress?.City,
        State = project.BillingAddress?.State,
        PostalCode = project.BillingAddress?.PostalCode,
    };
}
