using Microsoft.EntityFrameworkCore;
using inventory_management.Models;
using inventory_management.Data;
using System.Security.Cryptography;

namespace inventory_management.Services;

public interface IShelfUnitStore
{
    IReadOnlyList<ShelfUnit> GetAll();
    ShelfUnit? GetById(Guid id);
    string GetNextIdentifier();
    string GenerateUniqueBarcode();
    bool Exists(string identifier);
    bool ExistsForDifferentUnit(Guid id, string identifier);
    void Add(ShelfUnit unit);
    bool Update(ShelfUnit unit);
    void Delete(ShelfUnit unit);
}

public class ShelfUnitStore : IShelfUnitStore
{
    private readonly ApplicationDbContext _dbContext;

    public ShelfUnitStore(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public IReadOnlyList<ShelfUnit> GetAll()
    {
        return _dbContext.ShelfUnits
            .AsNoTracking()
            .ToList()
            .OrderBy(u => IdentifierToIndex(u.Identifier))
            .ThenBy(u => u.Identifier)
            .ToList();
    }

    public string GetNextIdentifier()
    {
        var used = _dbContext.ShelfUnits
            .AsNoTracking()
            .Select(u => u.Identifier.ToUpper())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = 0;
        while (true)
        {
            var candidate = IndexToIdentifier(index);
            if (!used.Contains(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    public bool Exists(string identifier)
    {
        return _dbContext.ShelfUnits
            .Any(u => u.Identifier.ToUpper() == identifier.ToUpper());
    }

    public ShelfUnit? GetById(Guid id)
    {
        return _dbContext.ShelfUnits.FirstOrDefault(u => u.Id == id);
    }

    public bool ExistsForDifferentUnit(Guid id, string identifier)
    {
        return _dbContext.ShelfUnits.Any(u =>
            u.Id != id &&
            u.Identifier.ToUpper() == identifier.ToUpper());
    }

    public string GenerateUniqueBarcode()
    {
        var used = _dbContext.ShelfUnits
            .AsNoTracking()
            .Select(u => u.BarcodeNumber)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            // Format: 2 + 12 random digits + 0
            var middle = string.Concat(Enumerable.Range(0, 12)
                .Select(_ => RandomNumberGenerator.GetInt32(0, 10).ToString()));
            var candidate = $"2{middle}0";

            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    public void Add(ShelfUnit unit)
    {
        _dbContext.ShelfUnits.Add(unit);
        _dbContext.SaveChanges();
    }

    public bool Update(ShelfUnit unit)
    {
        var existing = _dbContext.ShelfUnits.FirstOrDefault(u => u.Id == unit.Id);
        if (existing is null)
        {
            return false;
        }

        existing.Identifier = unit.Identifier;
        existing.ShelfCount = unit.ShelfCount;
        existing.StorageUnitId = unit.StorageUnitId;
        existing.Status = unit.Status;
        _dbContext.SaveChanges();

        return true;
    }

    public void Delete(ShelfUnit unit)
    {
        var existing = _dbContext.ShelfUnits.FirstOrDefault(u => u.Id == unit.Id);
        if (existing is not null)
        {
            _dbContext.ShelfUnits.Remove(existing);
        }

        // SaveChanges here also flushes any other pending tracked changes on this request's
        // DbContext (e.g. ShelfController.Delete's box-unlink updates), same as Add/Update above.
        _dbContext.SaveChanges();
    }

    private static string IndexToIdentifier(int index)
    {
        var value = index + 1;
        var chars = new Stack<char>();

        while (value > 0)
        {
            value--;
            var letter = (char)('A' + (value % 26));
            chars.Push(letter);
            value /= 26;
        }

        return new string(chars.ToArray());
    }

    private static int IdentifierToIndex(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return int.MaxValue;
        }

        var value = 0;
        foreach (var c in identifier.ToUpperInvariant())
        {
            if (c < 'A' || c > 'Z')
            {
                return int.MaxValue;
            }

            value = value * 26 + (c - 'A' + 1);
        }

        return value - 1;
    }
}
