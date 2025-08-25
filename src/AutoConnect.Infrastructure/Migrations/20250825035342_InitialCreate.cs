using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoConnect.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    VpnCertificate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LastKnownIpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    LastConnectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientId = table.Column<Guid>(type: "uuid", nullable: false),
                    VIN = table.Column<string>(type: "character varying(17)", maxLength: 17, nullable: true),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionEndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConnectionStatus = table.Column<int>(type: "integer", nullable: false),
                    ObdAdapterType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ObdProtocol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PingLatencyMs = table.Column<int>(type: "integer", nullable: true),
                    DataUsageMB = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastDataReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleSessions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VehicleData",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BatteryVoltage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    KL15Voltage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    KL30Voltage = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    IgnitionStatus = table.Column<int>(type: "integer", nullable: false),
                    EngineRPM = table.Column<int>(type: "integer", nullable: true),
                    VehicleSpeed = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    CoolantTemperature = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    FuelLevel = table.Column<decimal>(type: "numeric(5,1)", nullable: true),
                    DiagnosticTroubleCodes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RawObdData = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleData_VehicleSessions_VehicleSessionId",
                        column: x => x.VehicleSessionId,
                        principalTable: "VehicleSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_Email",
                table: "Clients",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleData_Timestamp",
                table: "VehicleData",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleData_VehicleSessionId_Timestamp",
                table: "VehicleData",
                columns: new[] { "VehicleSessionId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleSessions_ClientId_SessionStartedAt",
                table: "VehicleSessions",
                columns: new[] { "ClientId", "SessionStartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleSessions_VIN",
                table: "VehicleSessions",
                column: "VIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleData");

            migrationBuilder.DropTable(
                name: "VehicleSessions");

            migrationBuilder.DropTable(
                name: "Clients");
        }
    }
}
