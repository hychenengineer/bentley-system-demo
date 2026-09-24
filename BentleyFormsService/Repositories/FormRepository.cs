using Microsoft.EntityFrameworkCore;
using BentleyFormsService.Data;
using BentleyFormsService.Models.Entities;

namespace BentleyFormsService.Repositories;

public interface IFormRepository
{
    Task<FormDataEntity?> GetByIdAsync(string id);
    Task<List<FormDataEntity>> ListAsync(string? iTwinId, string? formDefinitionId, int top = 50, int skip = 0);
    Task<FormDataEntity> CreateAsync(FormDataEntity form);
    Task UpdateAsync(FormDataEntity form);
    Task<bool> DeleteAsync(string id);
}

public class FormRepository : IFormRepository
{
    private readonly FormsDbContext _context;

    public FormRepository(FormsDbContext context)
    {
        _context = context;
    }

    public async Task<FormDataEntity?> GetByIdAsync(string id)
    {
        return await _context.Forms
            .Include(f => f.AuditLogs)
            .FirstOrDefaultAsync(f => f.Id == id);
    }

    public async Task<List<FormDataEntity>> ListAsync(string? iTwinId, string? formDefinitionId, int top = 50, int skip = 0)
    {
        var query = _context.Forms
            .Include(f => f.AuditLogs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(iTwinId))
        {
            query = query.Where(f => f.ITwinId == iTwinId);
        }

        if (!string.IsNullOrWhiteSpace(formDefinitionId))
        {
            query = query.Where(f => f.FormDefinitionId == formDefinitionId);
        }

        return await query
            .OrderByDescending(f => f.CreatedDateTime)
            .Skip(skip)
            .Take(top)
            .ToListAsync();
    }

    public async Task<FormDataEntity> CreateAsync(FormDataEntity form)
    {
        await _context.Forms.AddAsync(form);
        await _context.SaveChangesAsync();
        return form;
    }

    public async Task UpdateAsync(FormDataEntity form)
    {
        form.LastModifiedDateTime = DateTime.UtcNow;
        _context.Forms.Update(form);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var existing = await _context.Forms.FindAsync(id);
        if (existing == null) return false;

        _context.Forms.Remove(existing);
        await _context.SaveChangesAsync();
        return true;
    }
}
