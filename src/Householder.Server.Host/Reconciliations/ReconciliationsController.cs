using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CQRS.Command.Abstractions;
using CQRS.Query.Abstractions;
using Householder.Server.Expenses;
using Householder.Server.Reconciliations;
using Householder.Server.Settlements;
using Householder.Server.Users;
using Microsoft.AspNetCore.Authorization;
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
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<ActionResult<long>> InsertReconciliation([FromBody] InsertReconciliationCommand command)
        {
            // Get Expenses
            var expenseQuery = new GetExpensesQuery { Status = 0 };
            var expenses = await queryProcessor.ExecuteAsync(expenseQuery);

            if (expenses.Count() == 0)
            {
                return NoContent();
            }

            await commandProcessor.ExecuteAsync(command);
            var reconciliationId = command.Id;

            // TODO: Put this logic into dedicated command handlers
            // TODO: All of the below should be one transaction

            // Get Residents
            var allUsersQuery = new GetAllUsersQuery();
            var users = await queryProcessor.ExecuteAsync(allUsersQuery);

            // Build settlements
            var settlementBuilder = new SettlementBuilder(reconciliationId, users);
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

        [HttpGet]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<IEnumerable<ReconciliationWithSettlementsDTO>>> GetReconciliations([FromRoute] GetReconciliationsQuery query)
        {
            var results = await queryProcessor.ExecuteAsync(query);

            foreach (var reconciliation in results)
            {
                var settlementQuery = new GetSettlementsByReconciliationIdQuery { ReconciliationId = reconciliation.Id };
                var settlements = await queryProcessor.ExecuteAsync(settlementQuery);
                reconciliation.Settlements = settlements;
            }

            return Ok(results);
        }

        [HttpGet("{id}")]
        [Authorize]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ReconciliationWithSettlementsDTO>> GetReconciliationById([FromRoute] GetReconciliationByIdQuery query)
        {
            var result = await queryProcessor.ExecuteAsync(query);

            if (result != null)
            {
                var settlementQuery = new GetSettlementsByReconciliationIdQuery { ReconciliationId = result.Id };
                var settlements = await queryProcessor.ExecuteAsync(settlementQuery);
                result.Settlements = settlements;
                return Ok(result);
            }
            else
            {
                return NotFound();
            }
            
        }
    }
}