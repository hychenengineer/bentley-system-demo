using Microsoft.EntityFrameworkCore;
using BentleyFormsService.Data;
using BentleyFormsService.Models.Entities;

namespace BentleyFormsService.Repositories;

public interface IFormDefinitionRepository
{
    Task<FormDefinitionEntity?> GetByIdAsync(string id);
    Task<List<FormDefinitionEntity>> ListByITwinAsync(string? iTwinId);
}

public class FormDefinitionRepository : IFormDefinitionRepository
{
    private readonly FormsDbContext _context;

    public FormDefinitionRepository(FormsDbContext context)
    {
        _context = context;
    }

    public async Task<FormDefinitionEntity?> GetByIdAsync(string id)
    {
        return await _context.FormDefinitions
            .Include(f => f.Fields)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<List<FormDefinitionEntity>> ListByITwinAsync(string? iTwinId)
    {
        var query = _context.FormDefinitions
            .Include(f => f.Fields)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(iTwinId))
        {
            query = query.Where(f => f.ITwinId == iTwinId);
        }

        return await query.ToListAsync();
    }
}

public interface IWorkflowRepository
{
    Task<WorkflowDefinitionEntity?> GetByTypeAsync(string workflowType);
}

public class WorkflowRepository : IWorkflowRepository
{
    private readonly FormsDbContext _context;

    public WorkflowRepository(FormsDbContext context)
    {
        _context = context;
    }

    public async Task<WorkflowDefinitionEntity?> GetByTypeAsync(string workflowType)
    {
        return await _context.Workflows
            .Include(w => w.States)
            .FirstOrDefaultAsync(w => w.WorkflowType == workflowType);
    }
}
