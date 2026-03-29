# Backend Scaffold Commands (C# .NET 8)

Use this reference to bootstrap the V1 Backend solution via the .NET CLI.
Open a terminal (e.g., PowerShell) and follow these exact steps relative to the project root (`.../Alpla Angola Gerencial/`).

---

### Step 1: Create the Folder Structure

```powershell
# Create backend directory
mkdir -p src/backend
cd src/backend
```

### Step 2: Create the Solution and Projects

```powershell
# 1. Create the blank solution file
dotnet new sln -n AlplaPortal

# 2. Create the Clean Architecture layers
dotnet new webapi -n AlplaPortal.Api --use-controllers
dotnet new classlib -n AlplaPortal.Application
dotnet new classlib -n AlplaPortal.Domain
dotnet new classlib -n AlplaPortal.Infrastructure

# 3. Add all projects to the solution
dotnet sln add AlplaPortal.Api/AlplaPortal.Api.csproj
dotnet sln add AlplaPortal.Application/AlplaPortal.Application.csproj
dotnet sln add AlplaPortal.Domain/AlplaPortal.Domain.csproj
dotnet sln add AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj
```

### Step 3: Configure Project References (Dependencies)

```powershell
# Api depends on Application and Infrastructure
dotnet add AlplaPortal.Api/AlplaPortal.Api.csproj reference AlplaPortal.Application/AlplaPortal.Application.csproj
dotnet add AlplaPortal.Api/AlplaPortal.Api.csproj reference AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj

# Infrastructure depends on Application (and Domain implicitly)
dotnet add AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj reference AlplaPortal.Application/AlplaPortal.Application.csproj

# Application depends exclusively on Domain (Core Business Logic)
dotnet add AlplaPortal.Application/AlplaPortal.Application.csproj reference AlplaPortal.Domain/AlplaPortal.Domain.csproj
```

### Step 4: Install Required NuGet Packages

```powershell
# 1. Infrastructure (Database drivers)
dotnet add AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj package Microsoft.EntityFrameworkCore.SqlServer -v 8.0.*
dotnet add AlplaPortal.Infrastructure/AlplaPortal.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design -v 8.0.*

# 2. Application (CQRS & Validation)
dotnet add AlplaPortal.Application/AlplaPortal.Application.csproj package MediatR -v 12.*
dotnet add AlplaPortal.Application/AlplaPortal.Application.csproj package FluentValidation.DependencyInjectionExtensions -v 11.*

# 3. API (Logging & Serialization tools)
dotnet add AlplaPortal.Api/AlplaPortal.Api.csproj package Serilog.AspNetCore -v 8.0.*
```

### Step 5: Clean up default generated files

```powershell
# Remove boilerplate files we don't need
rm AlplaPortal.Api/Controllers/WeatherForecastController.cs
rm AlplaPortal.Api/WeatherForecast.cs
rm AlplaPortal.Application/Class1.cs
rm AlplaPortal.Domain/Class1.cs
rm AlplaPortal.Infrastructure/Class1.cs

# Create proper layer folders
mkdir -p AlplaPortal.Application/DTOs
mkdir -p AlplaPortal.Application/Interfaces
mkdir -p AlplaPortal.Application/Requests

mkdir -p AlplaPortal.Domain/Entities
mkdir -p AlplaPortal.Domain/Enums

mkdir -p AlplaPortal.Infrastructure/Data
mkdir -p AlplaPortal.Infrastructure/Services
```

---

## What to do next (After Scaffolding)

1. **Entity Framework Tooling:** Ensure your developer machine has the EF Core CLI tools installed globally:

   ```powershell
   dotnet tool install --global dotnet-ef --version 8.0.*
   ```

2. **Domain Classes:** Create your C# mapped entity classes (e.g., `Request.cs`, `RequestLineItem.cs`) inside `AlplaPortal.Domain/Entities`.

3. **DbContext:** Create `ApplicationDbContext.cs` inside `AlplaPortal.Infrastructure/Data`.

4. **First Migration:** Once entities and Context are created, run this command from the `src/backend` directory to snapshot the SQL schema:

   ```powershell
   dotnet ef migrations add InitialCreate --project AlplaPortal.Infrastructure --startup-project AlplaPortal.Api
   ```

5. **Update Local DB:** Apply the tables to the SQL server specified in `appsettings.Development.json`:

   ```powershell
   dotnet ef database update --project AlplaPortal.Infrastructure --startup-project AlplaPortal.Api
   ```
