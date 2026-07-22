using Crosscheck.Application.Projects;
using Crosscheck.Domain.Entities;

namespace Crosscheck.UnitTests.Projects;

public class ProjectBillingTests
{
    private static Client Client() => new()
    {
        Id = Guid.NewGuid(),
        Name = "MDWFP",
        BillingContactName = "Agency AP",
        BillingContactEmail = "ap@mdwfp.example",
        BillingAddress = new BillingAddress { Line1 = "1505 Eastover Dr", City = "Jackson" },
        PaymentTermsDays = 45,
    };

    private static Project Project() => new()
    {
        Id = Guid.NewGuid(),
        ClientId = Guid.NewGuid(),
        Name = "P",
        Code = "MDWFP-01",
    };

    [Fact]
    public void Falls_back_to_client_when_project_has_no_overrides()
    {
        var profile = ProjectBilling.Resolve(Project(), Client());

        Assert.Equal("Agency AP", profile.ContactName);
        Assert.Equal("ap@mdwfp.example", profile.ContactEmail);
        Assert.Equal("Jackson", profile.Address!.City);
        Assert.Equal(45, profile.PaymentTermsDays);
        Assert.False(profile.ContactFromProject);
        Assert.False(profile.TermsFromProject);
    }

    [Fact]
    public void Project_overrides_win_field_by_field()
    {
        var project = Project();
        project.BillingContactName = "Fisheries Div";
        project.PaymentTermsDays = 60;
        // email and address left null → inherit client

        var profile = ProjectBilling.Resolve(project, Client());

        Assert.Equal("Fisheries Div", profile.ContactName);      // project
        Assert.Equal("ap@mdwfp.example", profile.ContactEmail);  // client
        Assert.Equal("Jackson", profile.Address!.City);          // client
        Assert.Equal(60, profile.PaymentTermsDays);              // project
        Assert.True(profile.ContactFromProject);
        Assert.True(profile.TermsFromProject);
    }

    [Fact]
    public void Terms_default_to_30_when_neither_project_nor_client_set_them()
    {
        var client = Client();
        client.PaymentTermsDays = null;

        var profile = ProjectBilling.Resolve(Project(), client);

        Assert.Equal(ProjectBilling.DefaultPaymentTermsDays, profile.PaymentTermsDays);
        Assert.Equal(30, profile.PaymentTermsDays);
    }

    [Fact]
    public void Works_when_client_is_missing()
    {
        var project = Project();
        project.BillingContactName = "Only Project";

        var profile = ProjectBilling.Resolve(project, client: null);

        Assert.Equal("Only Project", profile.ContactName);
        Assert.Null(profile.ContactEmail);
        Assert.Equal(30, profile.PaymentTermsDays);
    }
}
