# ── Stage 1: Build ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first (layer caching)
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the source code
COPY . .

# Publish the release build
RUN dotnet publish -c Release -o /app/publish

# ── Stage 2: Runtime ─────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080

# Copy published output from build stage
COPY --from=build /app/publish .

# IMPORTANT: Make sure this DLL name matches your .csproj <AssemblyName>
# If your .csproj does not set <AssemblyName>, it defaults to the .csproj filename
# e.g. SpeechLiftWebAPI.csproj → SpeechLiftWebAPI.dll
ENTRYPOINT ["dotnet", "SpeechLiftWebAPI.dll"]