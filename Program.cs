using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using InstructorInformationSystem.Data;
using InstructorInformationSystem.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
var sqliteConnection = new SqliteConnectionStringBuilder(connectionString);
if (sqliteConnection.DataSource != ":memory:" && !Path.IsPathRooted(sqliteConnection.DataSource))
    sqliteConnection.DataSource = Path.Combine(builder.Environment.ContentRootPath, sqliteConnection.DataSource);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(sqliteConnection.ConnectionString));

builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

builder.Services.AddAuthorization();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    // Older databases may have tables created from an earlier model.
    // Add missing columns and normalize legacy account IDs.
    await db.Database.OpenConnectionAsync();
    try
    {
        var connection = db.Database.GetDbConnection();

        var requiredUserColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CreatedAt"] = "TEXT NOT NULL DEFAULT '1970-01-01 00:00:00'",
            ["AdminId"] = "TEXT NOT NULL DEFAULT ''",
            ["FirstName"] = "TEXT NOT NULL DEFAULT ''",
            ["FullName"] = "TEXT NOT NULL DEFAULT ''",
            ["LastName"] = "TEXT NOT NULL DEFAULT ''",
            ["Email"] = "TEXT NOT NULL DEFAULT ''",
            ["PasswordHash"] = "TEXT NOT NULL DEFAULT ''"
        };

        var existingUserColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('UserAccounts');";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingUserColumns.Add(reader.GetString(1));
        }

        foreach (var (columnName, columnDefinition) in requiredUserColumns)
        {
            if (existingUserColumns.Contains(columnName))
                continue;

            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE \"UserAccounts\" ADD COLUMN \"{columnName}\" {columnDefinition};";
            await command.ExecuteNonQueryAsync();
            app.Logger.LogInformation("Added missing UserAccounts.{ColumnName} column.", columnName);
        }

        var userAccounts = new List<(int Id, string AdminId)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT \"Id\", \"AdminId\" FROM \"UserAccounts\" ORDER BY \"Id\";";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                userAccounts.Add((reader.GetInt32(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        var usedNumericAdminIds = userAccounts
            .Where(account => account.AdminId.Length > 0 && account.AdminId.All(char.IsAsciiDigit))
            .Select(account => account.AdminId)
            .ToHashSet(StringComparer.Ordinal);
        var nextAvailableAdminId = 1;
        var normalizedAdminIds = 0;

        foreach (var account in userAccounts)
        {
            if (account.AdminId.Length > 0 && account.AdminId.All(char.IsAsciiDigit))
                continue;

            var numericAdminId = account.Id.ToString();
            if (!usedNumericAdminIds.Add(numericAdminId))
            {
                do
                {
                    numericAdminId = nextAvailableAdminId.ToString();
                    nextAvailableAdminId++;
                }
                while (!usedNumericAdminIds.Add(numericAdminId));
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE \"UserAccounts\" SET \"AdminId\" = $adminId WHERE \"Id\" = $id;";
            var adminIdParameter = command.CreateParameter();
            adminIdParameter.ParameterName = "$adminId";
            adminIdParameter.Value = numericAdminId;
            command.Parameters.Add(adminIdParameter);
            var userIdParameter = command.CreateParameter();
            userIdParameter.ParameterName = "$id";
            userIdParameter.Value = account.Id;
            command.Parameters.Add(userIdParameter);
            await command.ExecuteNonQueryAsync();
            normalizedAdminIds++;
        }

        if (normalizedAdminIds > 0)
            app.Logger.LogInformation("Assigned numeric Admin IDs to {AccountCount} legacy accounts.", normalizedAdminIds);

        var requiredInstructorColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FullName"] = "TEXT NOT NULL DEFAULT ''",
            ["Age"] = "INTEGER NOT NULL DEFAULT 0",
            ["Address"] = "TEXT NOT NULL DEFAULT ''",
            ["Designation"] = "TEXT NOT NULL DEFAULT ''",
            ["Gender"] = "TEXT NOT NULL DEFAULT ''",
            ["ContactNumber"] = "TEXT NOT NULL DEFAULT ''",
            ["Majorship"] = "TEXT NOT NULL DEFAULT ''",
            ["BachelorDegree"] = "TEXT NOT NULL DEFAULT ''",
            ["EmailAddress"] = "TEXT NOT NULL DEFAULT ''"
        };

        var existingInstructorColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('Instructors');";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                existingInstructorColumns.Add(reader.GetString(1));
        }

        foreach (var (columnName, columnDefinition) in requiredInstructorColumns)
        {
            if (existingInstructorColumns.Contains(columnName))
                continue;

            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE \"Instructors\" ADD COLUMN \"{columnName}\" {columnDefinition};";
            await command.ExecuteNonQueryAsync();
            app.Logger.LogInformation("Added missing Instructors.{ColumnName} column.", columnName);
        }

        if (existingInstructorColumns.Contains("FirstName")
            && existingInstructorColumns.Contains("LastName")
            && existingInstructorColumns.Contains("Email")
            && existingInstructorColumns.Contains("Phone"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE "Instructors"
                SET
                    "FullName" = CASE
                        WHEN trim("FullName") = '' AND trim(COALESCE("FirstName", '') || ' ' || COALESCE("LastName", '')) <> ''
                        THEN trim(COALESCE("FirstName", '') || ' ' || COALESCE("LastName", ''))
                        ELSE "FullName"
                    END,
                    "EmailAddress" = CASE
                        WHEN trim("EmailAddress") = '' THEN COALESCE("Email", '')
                        ELSE "EmailAddress"
                    END,
                    "ContactNumber" = CASE
                        WHEN trim("ContactNumber") = '' THEN COALESCE("Phone", '')
                        ELSE "ContactNumber"
                    END
                WHERE trim("FullName") = ''
                    OR trim("EmailAddress") = ''
                    OR trim("ContactNumber") = '';
                """;
            await command.ExecuteNonQueryAsync();
        }
    }
    finally
    {
        await db.Database.CloseConnectionAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// The project opens on the login page.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();