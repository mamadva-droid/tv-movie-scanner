# Stage 1: Build .NET 8 App
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["Antigravity Progect.csproj", "./"]
RUN dotnet restore "./Antigravity Progect.csproj"
COPY . .
RUN dotnet publish "Antigravity Progect.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5000
ENV PORT=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "Antigravity Progect.dll"]
