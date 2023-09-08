# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:6.0 as build

WORKDIR /src
COPY ./src/discordBot/discordBot.csproj ./src/discordBot/
RUN dotnet restore ./src/discordBot/discordBot.csproj

COPY . .

WORKDIR /src
RUN dotnet publish ./src/discordBot/discordBot.csproj -c Release -o /app/publish

# Stage 2: Run
FROM mcr.microsoft.com/dotnet/aspnet:6.0 as run
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "discordBot.dll"]
