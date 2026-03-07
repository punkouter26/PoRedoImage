# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy build configuration files first for restore layer caching
COPY Directory.Build.props ./
COPY Directory.Packages.props ./

# Copy project file for NuGet restore caching
COPY src/PoRedoImage.Web/PoRedoImage.Web.csproj src/PoRedoImage.Web/

# Restore NuGet packages
RUN dotnet restore src/PoRedoImage.Web/PoRedoImage.Web.csproj

# Copy all source code
COPY src/ src/

# Publish (SixLabors.ImageSharp is cross-platform — no native libs required)
RUN dotnet publish src/PoRedoImage.Web/PoRedoImage.Web.csproj -c Release --no-restore -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install fonts required by MemeGeneratorService (SixLabors.Fonts needs TrueType fonts on Linux)
RUN apt-get update && apt-get install -y --no-install-recommends fonts-liberation && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PoRedoImage.Web.dll"]
