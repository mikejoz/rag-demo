# Build context must be the repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/RagChatDemo.ConfluenceStub/RagChatDemo.ConfluenceStub.csproj src/RagChatDemo.ConfluenceStub/
RUN dotnet restore src/RagChatDemo.ConfluenceStub/RagChatDemo.ConfluenceStub.csproj

COPY src/RagChatDemo.ConfluenceStub/ src/RagChatDemo.ConfluenceStub/
RUN dotnet publish src/RagChatDemo.ConfluenceStub/RagChatDemo.ConfluenceStub.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "RagChatDemo.ConfluenceStub.dll"]
