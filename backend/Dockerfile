FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["AltusIQ.Api.csproj", "."]
RUN dotnet restore AltusIQ.Api.csproj
COPY . .
# Name the project explicitly: backend/ also holds AltusIQ.sln, and a bare
# publish errors out as ambiguous. The runtime image never wants the tests.
RUN dotnet publish AltusIQ.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AltusIQ.Api.dll"]