namespace Orkabi.Web.Tests.Infrastructure;

public static class TestLogin
{
    /// <summary>
    /// Creates an HttpClient (AllowAutoRedirect=false), logs in via the anti-forgery+POST
    /// flow at /Account/Login, and returns the same client carrying the orkabi.auth cookie.
    /// </summary>
    public static async Task<HttpClient> SignInAsync(OrkabiAppFactory factory, string email, string password)
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var getResp = await client.GetAsync("/Account/Login");
        var html = await getResp.Content.ReadAsStringAsync();
        var token = AntiForgery.Extract(html);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email,
            ["Password"] = password,
            ["__RequestVerificationToken"] = token
        });

        await client.PostAsync("/Account/Login", form);

        return client;
    }
}
