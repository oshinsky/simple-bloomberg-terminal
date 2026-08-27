using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Contributions;

// Identifies the contributor and whether writes can go live. The controller builds this context so the
// writer remains independent of HTTP concerns.
public readonly record struct Contributor(bool IsReviewer, string? UserId)
{
    public ContributionStatus NewStatus => IsReviewer ? ContributionStatus.Approved : ContributionStatus.Pending;
    // A new live row carries no contributor; a pending proposal records who proposed it.
    public string? StampUserId => IsReviewer ? null : UserId;
}

// Centralizes contribution writes, proof, mirrored links, and review transitions so every source type
// follows the same approval and supersession rules.
public interface IContributionWriter
{
    // Creates or updates the active source and returns its ID. Invalid classifications return null;
    // omitted proof fields preserve the existing citation.
    long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by,
        string? reference = null, string? evidence = null, long? filingId = null);

    // Create the mirror source on the counterparty pointing back at owner, unless one already exists.
    void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName,
        double? value, Contributor by);

    void Approve(string type, IEnumerable<long> ids);
    void Reject(string type, IEnumerable<long> ids);
}

public class ContributionWriter(
    IRevenueSourceRepository revenue, ICostSourceRepository cost, ICompanyRiskRepository risks)
    : IContributionWriter
{
    // Bundles the proof location, quote, and filing so node-specific upserts share one parameter.
    private readonly record struct Proof(string? Reference, string? Evidence, long? FilingId);

    public long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by,
        string? reference = null, string? evidence = null, long? filingId = null)
    {
        var proof = new Proof(reference, evidence, filingId);
        return node switch
        {
            ExtractionNode.COST => UpsertCost(companyId, rowId, classification, name, value, percentage, relatedCompanyId, proof, by),
            ExtractionNode.RISK => UpsertRisk(companyId, rowId, classification, name, note, proof, by),
            _                   => UpsertRevenue(companyId, rowId, classification, name, value, percentage, relatedCompanyId, proof, by),
        };
    }

    private long? UpsertRevenue(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Proof proof, Contributor by)
    {
        if (!Enum.TryParse<SourceType>(classification, out var sourceType)) return null;
        if (rowId is { } id)
        {
            var existing = revenue.GetById(id);
            if (existing is null || existing.CompanyId != companyId) return null;
            // Non-reviewers propose a pending replacement; approval later retires the live row.
            if (!by.IsReviewer)
            {
                var proposal = new RevenueSource(sourceType, name, companyId)
                {
                    Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
                    Reference = proof.Reference ?? existing.Reference,
                    Evidence = proof.Evidence ?? existing.Evidence,
                    FilingId = proof.FilingId ?? existing.FilingId,
                    DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                revenue.Add(proposal);
                return proposal.Id;
            }
            existing.SourceType = sourceType;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            revenue.Update(existing);
            return existing.Id;
        }
        var row = new RevenueSource(sourceType, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        revenue.Add(row);
        return row.Id;
    }

    private long? UpsertCost(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Proof proof, Contributor by)
    {
        if (!Enum.TryParse<CostBase>(classification, out var costBase)) return null;
        if (rowId is { } id)
        {
            var existing = cost.GetById(id);
            if (existing is null || existing.CompanyId != companyId) return null;
            // Non-reviewer edit: propose a superseding Pending copy, leave the live row untouched.
            if (!by.IsReviewer)
            {
                var proposal = new CostSource(costBase, name, companyId)
                {
                    Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
                    Reference = proof.Reference ?? existing.Reference,
                    Evidence = proof.Evidence ?? existing.Evidence,
                    FilingId = proof.FilingId ?? existing.FilingId,
                    DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                cost.Add(proposal);
                return proposal.Id;
            }
            existing.CostBase = costBase;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            cost.Update(existing);
            return existing.Id;
        }
        var row = new CostSource(costBase, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        cost.Add(row);
        return row.Id;
    }

    private long? UpsertRisk(long companyId, long? rowId, string classification, string name, string? note, Proof proof, Contributor by)
    {
        if (!Enum.TryParse<RiskScope>(classification, out var scope)) return null;
        if (rowId is { } id)
        {
            var existing = risks.GetById(id);
            if (existing is null || existing.CompanyId != companyId) return null;
            // Non-reviewer edit: propose a superseding Pending copy, leave the live row untouched.
            if (!by.IsReviewer)
            {
                var proposal = new CompanyRisk(scope, name, companyId)
                {
                    Note = note,
                    Reference = proof.Reference ?? existing.Reference,
                    Evidence = proof.Evidence ?? existing.Evidence,
                    FilingId = proof.FilingId ?? existing.FilingId,
                    DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                risks.Add(proposal);
                return proposal.Id;
            }
            existing.Scope = scope;
            existing.Name = name;
            existing.Note = note;
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            risks.Update(existing);
            return existing.Id;
        }
        var row = new CompanyRisk(scope, name, companyId)
        {
            Note = note,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        risks.Add(row);
        return row.Id;
    }

    // Applies only supplied proof fields so an edit without a citation preserves the existing one.
    private static void ApplyProof(Proof proof, Action<string> setReference, Action<string> setEvidence,
        Action<long> setFilingId)
    {
        if (proof.Reference is not null) setReference(proof.Reference);
        if (proof.Evidence is not null) setEvidence(proof.Evidence);
        if (proof.FilingId is { } filingId) setFilingId(filingId);
    }

    public void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName,
        double? value, Contributor by)
    {
        var (mirror, classification) = node == ExtractionNode.COST
            ? (ExtractionNode.REVENUE, nameof(SourceType.CUSTOMER))
            : (ExtractionNode.COST, nameof(CostBase.COGS));

        var exists = mirror == ExtractionNode.COST
            ? cost.HasRelatedCompany(counterpartyId, ownerId)
            : revenue.HasRelatedCompany(counterpartyId, ownerId);
        if (exists) return;

        UpsertRow(mirror, counterpartyId, null, classification, ownerName,
            value: value, percentage: null, note: null, relatedCompanyId: ownerId, by);
    }

    public void Approve(string type, IEnumerable<long> ids)
    {
        switch (type)
        {
            case "REVENUE": Approve(ids, revenue.GetById, revenue.SoftDelete, revenue.Update); break;
            case "COST": Approve(ids, cost.GetById, cost.SoftDelete, cost.Update); break;
            case "RISK": Approve(ids, risks.GetById, risks.SoftDelete, risks.Update); break;
        }
    }

    public void Reject(string type, IEnumerable<long> ids)
    {
        switch (type)
        {
            case "REVENUE": Reject(ids, revenue.GetById, revenue.Update); break;
            case "COST": Reject(ids, cost.GetById, cost.Update); break;
            case "RISK": Reject(ids, risks.GetById, risks.Update); break;
        }
    }

    // Approval retires the superseded row and publishes the pending replacement. Ignoring non-pending
    // IDs keeps repeated submissions safe.
    private static void Approve<T>(
        IEnumerable<long> ids, Func<long, T?> getById, Action<long> softDelete, Action<T> update)
        where T : IContribution
    {
        foreach (var id in ids)
            if (getById(id) is { Status: ContributionStatus.Pending } row)
            {
                if (row.SupersedesId is { } supersededId) softDelete(supersededId);
                row.Status = ContributionStatus.Approved;
                update(row);
            }
    }

    // Rejection removes a proposal from the review queue without changing the live row it targeted.
    private static void Reject<T>(IEnumerable<long> ids, Func<long, T?> getById, Action<T> update)
        where T : IContribution
    {
        foreach (var id in ids)
            if (getById(id) is { Status: ContributionStatus.Pending } row)
            {
                row.Status = ContributionStatus.Rejected;
                update(row);
            }
    }
}
