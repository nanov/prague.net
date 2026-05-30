namespace Prague.Api.UI;

using Prague.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

/// <summary>
/// Extension methods for configuring the Prague Cache UI.
/// </summary>
public static class CacheUIExtensions {
	/// <summary>
	/// Adds Prague UI services to the service collection.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <returns>The service collection for chaining.</returns>
	public static IServiceCollection AddPragueUI(this IServiceCollection services) {
		services.AddRazorComponents();
		services.AddMudServices();
		return services;
	}

	/// <summary>
	/// Adds Prague UI services to the web application builder.
	/// </summary>
	/// <param name="builder">The web application builder.</param>
	/// <returns>The web application builder for chaining.</returns>
	public static WebApplicationBuilder AddPragueUI(this WebApplicationBuilder builder) {
		builder.Services.AddPragueUI();
		builder.WebHost.UseStaticWebAssets();
		return builder;
	}

	/// <summary>
	/// Maps Prague UI endpoints and middleware to the web application.
	/// </summary>
	/// <param name="app">The web application.</param>
	/// <returns>The web application for chaining.</returns>
	public static WebApplication MapPragueUI(this WebApplication app) {
		app.MapPragueEndpoints();
		app.UseStaticFiles();
		app.UseRouting();
		app.UseAntiforgery();
		app.MapRazorComponents<CacheApp>();
		return app;
	}
}
