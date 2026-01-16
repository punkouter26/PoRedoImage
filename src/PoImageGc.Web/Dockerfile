# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src

# Copy Directory.Packages.props and solution files
COPY Directory.Packages.props ./
COPY PoImageGc.slnx ./

# Copy project files
COPY src/PoImageGc.Web/PoImageGc.Web.csproj src/PoImageGc.Web/
COPY src/PoImageGc.Shared/PoImageGc.Shared.csproj src/PoImageGc.Shared/
COPY src/PoImageGc.ServiceDefaults/PoImageGc.ServiceDefaults.csproj src/PoImageGc.ServiceDefaults/

# Restore packages
RUN dotnet restore src/PoImageGc.Web/PoImageGc.Web.csproj

# Copy all source code
COPY src/ src/

# Build and publish
RUN dotnet publish src/PoImageGc.Web/PoImageGc.Web.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS runtime
WORKDIR /app

# Install libgdiplus for System.Drawing (meme generation)
RUN apt-get update && apt-get install -y libgdiplus libc6-dev && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "PoImageGc.Web.dll"]
