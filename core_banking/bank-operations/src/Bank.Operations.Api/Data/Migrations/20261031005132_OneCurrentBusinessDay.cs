using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bank.Operations.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OneCurrentBusinessDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // At most one business day is not Closed. Deferred to commit time, because the handler closes day D and
            // inserts D+1 in one SaveChanges, and EF does not promise the order of the UPDATE and the INSERT.
            // EF Core cannot model a partial, deferrable exclusion constraint, so it is SQL here.
            migrationBuilder.Sql("""
                ALTER TABLE business_day
                    ADD CONSTRAINT ex_business_day_one_current
                    EXCLUDE USING btree ((true) WITH =) WHERE (status <> 'Closed')
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE business_day DROP CONSTRAINT ex_business_day_one_current;");
        }
    }
}
