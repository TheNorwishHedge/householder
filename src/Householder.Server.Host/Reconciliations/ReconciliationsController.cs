using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CQRS.Command.Abstractions;
using CQRS.Query.Abstractions;
using Householder.Server.Expenses;
using Householder.Server.Models;
using Householder.Server.Reconciliations;
using Householder.Server.Residents;
using Householder.Server.Settlements;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Householder.Server.Host.Reconciliations
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReconciliationsController : ControllerBase
    {
        private readonly ILogger<ReconciliationsController> logger;
        private readonly IQueryExecutor queryProcessor;
        private readonly ICommandExecutor commandProcessor;

        public ReconciliationsController(IQueryExecutor queryProcessor, ICommandExecutor commandProcessor, ILogger<ReconciliationsController> logger)
        {
            this.commandProcessor = commandProcessor;
            this.queryProcessor = queryProcessor;
            this.logger = logger;
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status304NotModified)]
        public async Task<ActionResult<long>> InsertReconciliation([FromBody] InsertReconciliationCommand command)
        {
            await commandProcessor.ExecuteAsync(command);
            var reconciliationId = command.Id;

            // TODO: Put this logic somewhere else?
            // TODO: All of the below should be one transaction

            // Get Residents
            var residentsQuery = new GetResidentsQuery();
            var residents = await queryProcessor.ExecuteAsync(residentsQuery);

            // Get Expenses
            var expenseQuery = new GetExpensesQuery { Status = 0 };
            var expenses = await queryProcessor.ExecuteAsync(expenseQuery);

            // Build settlements
            var settlementBuilder = new SettlementBuilder(reconciliationId, residents);
            settlementBuilder.AddExpenses(expenses);
            var settlementCommands = settlementBuilder.Build();

            // Insert settlements
            foreach (var settlementCommand in settlementCommands)
            {
                await commandProcessor.ExecuteAsync(settlementCommand);
            }

            // Update expense status
            foreach (var expense in expenses)
            {
                var updateExpenseStatusCommand = new UpdateExpenseStatusCommand { Id = expense.Id, Status = ExpenseStatus.InProgress };
                await commandProcessor.ExecuteAsync(updateExpenseStatusCommand);
            }

            return Ok();
        }
    }
}