using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crosscheck.Application.Projects;
using Crosscheck.Domain;
using Crosscheck.Web.Models;

namespace Crosscheck.Web.Controllers;

public class HomeController(PortfolioDashboardService portfolioDashboard) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View();
        }

        // Managers land on the portfolio dashboard; the timesheet is the working home for everyone else.
        if (User.IsInRole(RoleNames.Admin) || User.IsInRole(RoleNames.OperationsManager) || User.IsInRole(RoleNames.ProjectManager))
        {
            return View("Dashboard", await portfolioDashboard.GetAsync(cancellationToken: cancellationToken));
        }

        return RedirectToAction("Index", "Timesheet");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [Authorize]
    public IActionResult Me()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
