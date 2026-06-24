using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Orkabi.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShiftTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClassId = table.Column<int>(type: "integer", nullable: false),
                    DefaultInstructorId = table.Column<int>(type: "integer", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<string>(type: "text", nullable: false),
                    EndTime = table.Column<string>(type: "text", nullable: false),
                    AcademicYearId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTemplates_AcademicYears_AcademicYearId",
                        column: x => x.AcademicYearId,
                        principalTable: "AcademicYears",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTemplates_AspNetUsers_DefaultInstructorId",
                        column: x => x.DefaultInstructorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftTemplates_Classes_ClassId",
                        column: x => x.ClassId,
                        principalTable: "Classes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftInstances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TemplateId = table.Column<int>(type: "integer", nullable: false),
                    ActualInstructorId = table.Column<int>(type: "integer", nullable: true),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftInstances_AspNetUsers_ActualInstructorId",
                        column: x => x.ActualInstructorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ShiftInstances_ShiftTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ShiftTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftInstanceId = table.Column<int>(type: "integer", nullable: false),
                    ModelId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InstructorNotes = table.Column<string>(type: "text", nullable: true),
                    ExpectedLessonsSnapshot = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonLogs_Models_ModelId",
                        column: x => x.ModelId,
                        principalTable: "Models",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonLogs_ShiftInstances_ShiftInstanceId",
                        column: x => x.ShiftInstanceId,
                        principalTable: "ShiftInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SubstitutionRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ShiftInstanceId = table.Column<int>(type: "integer", nullable: false),
                    RequestingInstructorId = table.Column<int>(type: "integer", nullable: false),
                    SubstituteInstructorId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ApprovedByUserId = table.Column<int>(type: "integer", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubstitutionRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubstitutionRequests_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubstitutionRequests_AspNetUsers_RequestingInstructorId",
                        column: x => x.RequestingInstructorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubstitutionRequests_AspNetUsers_SubstituteInstructorId",
                        column: x => x.SubstituteInstructorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubstitutionRequests_ShiftInstances_ShiftInstanceId",
                        column: x => x.ShiftInstanceId,
                        principalTable: "ShiftInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Attendances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LessonLogId = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Attendances_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Attendances_LessonLogs_LessonLogId",
                        column: x => x.LessonLogId,
                        principalTable: "LessonLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_ClientId",
                table: "Attendances",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_IdempotencyKey",
                table: "Attendances",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_LessonLogId_ClientId",
                table: "Attendances",
                columns: new[] { "LessonLogId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonLogs_ModelId",
                table: "LessonLogs",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonLogs_ShiftInstanceId",
                table: "LessonLogs",
                column: "ShiftInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftInstances_ActualInstructorId",
                table: "ShiftInstances",
                column: "ActualInstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftInstances_TemplateId_Date",
                table: "ShiftInstances",
                columns: new[] { "TemplateId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_AcademicYearId",
                table: "ShiftTemplates",
                column: "AcademicYearId");

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_ClassId_DayOfWeek_AcademicYearId",
                table: "ShiftTemplates",
                columns: new[] { "ClassId", "DayOfWeek", "AcademicYearId" });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTemplates_DefaultInstructorId",
                table: "ShiftTemplates",
                column: "DefaultInstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionRequests_ApprovedByUserId",
                table: "SubstitutionRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionRequests_RequestingInstructorId",
                table: "SubstitutionRequests",
                column: "RequestingInstructorId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionRequests_ShiftInstanceId",
                table: "SubstitutionRequests",
                column: "ShiftInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SubstitutionRequests_SubstituteInstructorId",
                table: "SubstitutionRequests",
                column: "SubstituteInstructorId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attendances");

            migrationBuilder.DropTable(
                name: "SubstitutionRequests");

            migrationBuilder.DropTable(
                name: "LessonLogs");

            migrationBuilder.DropTable(
                name: "ShiftInstances");

            migrationBuilder.DropTable(
                name: "ShiftTemplates");
        }
    }
}
