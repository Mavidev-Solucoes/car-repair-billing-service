using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceOrderCorrelation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payment_transactions_PaymentId",
                table: "payment_transactions");

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAtUtc",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceOrderId",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAtUtc",
                table: "budgets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceOrderId",
                table: "budgets",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Linhas criadas antes da saga não têm OS: cada orçamento ganha um id próprio (para o índice
            // único) e os pagamentos herdam o do orçamento.
            migrationBuilder.Sql(
                "UPDATE budgets SET \"ServiceOrderId\" = gen_random_uuid() " +
                "WHERE \"ServiceOrderId\" = '00000000-0000-0000-0000-000000000000';");
            migrationBuilder.Sql(
                "UPDATE payments p SET \"ServiceOrderId\" = b.\"ServiceOrderId\" FROM budgets b " +
                "WHERE p.\"BudgetId\" = b.\"Id\" AND p.\"ServiceOrderId\" = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.CreateIndex(
                name: "IX_payments_ServiceOrderId",
                table: "payments",
                column: "ServiceOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_budgets_ServiceOrderId",
                table: "budgets",
                column: "ServiceOrderId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_payments_ServiceOrderId",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "IX_budgets_ServiceOrderId",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "RefundedAtUtc",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "ServiceOrderId",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "CancelledAtUtc",
                table: "budgets");

            migrationBuilder.DropColumn(
                name: "ServiceOrderId",
                table: "budgets");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transactions_PaymentId",
                table: "payment_transactions",
                column: "PaymentId");
        }
    }
}
