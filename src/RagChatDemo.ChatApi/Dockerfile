# Build context must be the repository root (needed for the ProjectReference to Shared).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/RagChatDemo.Shared/RagChatDemo.Shared.csproj src/RagChatDemo.Shared/
COPY src/RagChatDemo.ChatApi/RagChatDemo.ChatApi.csproj src/RagChatDemo.ChatApi/
RUN dotnet restore src/RagChatDemo.ChatApi/RagChatDemo.ChatApi.csproj

COPY src/RagChatDemo.Shared/ src/RagChatDemo.Shared/
COPY src/RagChatDemo.ChatApi/ src/RagChatDemo.ChatApi/
RUN dotnet publish src/RagChatDemo.ChatApi/RagChatDemo.ChatApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "RagChatDemo.ChatApi.dll"]
