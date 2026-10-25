FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props SoporteBlunk.slnx ./
COPY src/SoporteBlunk.Domain/*.csproj src/SoporteBlunk.Domain/
COPY src/SoporteBlunk.Application/*.csproj src/SoporteBlunk.Application/
COPY src/SoporteBlunk.Infrastructure/*.csproj src/SoporteBlunk.Infrastructure/
COPY src/SoporteBlunk.Api/*.csproj src/SoporteBlunk.Api/
RUN dotnet restore src/SoporteBlunk.Api
COPY src ./src
RUN dotnet publish src/SoporteBlunk.Api -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "SoporteBlunk.Api.dll"]
