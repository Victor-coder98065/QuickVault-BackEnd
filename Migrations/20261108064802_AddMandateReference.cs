using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickVault.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMandateReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AffordabilityAssessments",
                columns: table => new
                {
                    AssessmentID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<int>(type: "int", nullable: false),
                    NetSalary = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MonthlyExpenses = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EmployerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmploymentType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaydayDate = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AffordabilityAssessments", x => x.AssessmentID);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IDNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Surname = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MobileNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EmailAddress = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CommunicationPreference = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CustomerAccountStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsBlacklisted = table.Column<bool>(type: "bit", nullable: false),
                    IsUnderDebtReview = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerID);
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                columns: table => new
                {
                    LoanID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerID = table.Column<int>(type: "int", nullable: false),
                    PrincipalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TermMonths = table.Column<int>(type: "int", nullable: false),
                    InitiationFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MonthlyServiceFee = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InterestRatePM = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalRepayment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BalanceRemaining = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LoanStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DebiCheckStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MandateReference = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdminNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.LoanID);
                });

            migrationBuilder.CreateTable(
                name: "OTPSessions",
                columns: table => new
                {
                    OTPID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IDNumber = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    OTPCode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsUsed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OTPSessions", x => x.OTPID);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AffordabilityAssessments");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Loans");

            migrationBuilder.DropTable(
                name: "OTPSessions");
        }
    }
}
