FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 5000

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["RagPipeline.csproj", "./"]
RUN dotnet restore "RagPipeline.csproj"
COPY . .
RUN dotnet build "RagPipeline.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "RagPipeline.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create logs directory
RUN mkdir -p /app/logs

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5000

ENTRYPOINT ["dotnet", "RagPipeline.dll", "--api"]
