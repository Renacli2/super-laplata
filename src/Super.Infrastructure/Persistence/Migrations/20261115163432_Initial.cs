using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Super.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    chain = table.Column<int>(type: "integer", nullable: false),
                    sepa_banner_id = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sepa_branch_id = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    street = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    locality = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    last_seen_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "import_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    chain = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rows_read = table.Column<int>(type: "integer", nullable: false),
                    rows_in_zone = table.Column<int>(type: "integer", nullable: false),
                    skipped_no_ean = table.Column<int>(type: "integer", nullable: false),
                    skipped_invalid_ean = table.Column<int>(type: "integer", nullable: false),
                    skipped_bad_price = table.Column<int>(type: "integer", nullable: false),
                    accepted = table.Column<int>(type: "integer", nullable: false),
                    branches = table.Column<int>(type: "integer", nullable: false),
                    distinct_products = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "products",
                columns: table => new
                {
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    brand = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    presentation_quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    presentation_unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    first_seen_on = table.Column<DateOnly>(type: "date", nullable: false),
                    last_seen_on = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.ean);
                });

            migrationBuilder.CreateTable(
                name: "branch_prices",
                columns: table => new
                {
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    branch_id = table.Column<int>(type: "integer", nullable: false),
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    list_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reference_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    promo_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branch_prices", x => new { x.price_date, x.branch_id, x.ean });
                    table.ForeignKey(
                        name: "fk_branch_prices_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_branch_prices_products_ean",
                        column: x => x.ean,
                        principalTable: "products",
                        principalColumn: "ean",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chain_prices",
                columns: table => new
                {
                    price_date = table.Column<DateOnly>(type: "date", nullable: false),
                    chain = table.Column<int>(type: "integer", nullable: false),
                    ean = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: false),
                    min_list_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    branch_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_prices", x => new { x.price_date, x.chain, x.ean });
                    table.ForeignKey(
                        name: "fk_chain_prices_products_ean",
                        column: x => x.ean,
                        principalTable: "products",
                        principalColumn: "ean",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_branch_prices_branch_id",
                table: "branch_prices",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_branch_prices_ean_price_date",
                table: "branch_prices",
                columns: new[] { "ean", "price_date" });

            migrationBuilder.CreateIndex(
                name: "ix_branches_chain_sepa_banner_id_sepa_branch_id",
                table: "branches",
                columns: new[] { "chain", "sepa_banner_id", "sepa_branch_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chain_prices_ean_price_date",
                table: "chain_prices",
                columns: new[] { "ean", "price_date" });

            migrationBuilder.CreateIndex(
                name: "ix_import_runs_price_date_chain",
                table: "import_runs",
                columns: new[] { "price_date", "chain" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "branch_prices");

            migrationBuilder.DropTable(
                name: "chain_prices");

            migrationBuilder.DropTable(
                name: "import_runs");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "products");
        }
    }
}
