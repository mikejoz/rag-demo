using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace RagChatDemo.Shared.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "SourceDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SourceUrl = table.Column<string>(type: "text", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IngestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Chunks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(768)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Chunks_SourceDocuments_SourceDocumentId",
                        column: x => x.SourceDocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_Embedding",
                table: "Chunks",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Chunks_SourceDocumentId",
                table: "Chunks",
                column: "SourceDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_ExternalId",
                table: "SourceDocuments",
                column: "ExternalId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Chunks");

            migrationBuilder.DropTable(
                name: "SourceDocuments");
        }
    }
}
