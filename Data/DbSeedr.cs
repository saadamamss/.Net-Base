using DataForge.Users;
using Microsoft.AspNetCore.Identity;

namespace DataForge.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<User>>();

        // Seed Roles
        string[] roles = ["Admin", "User", "Moderator"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed Admin User
        const string adminEmail = "admin@starter.com";
        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new User
            {
                Name = "Admin",
                Email = adminEmail,
                UserName = adminEmail,
                EmailVerified = true,
                IsActive = true
            };

            var result = await userManager.CreateAsync(admin, "Admin@123456");
            if (result.Succeeded)
                await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}