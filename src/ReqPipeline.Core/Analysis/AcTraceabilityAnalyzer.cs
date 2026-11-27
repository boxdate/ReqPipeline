using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Analysis;

public sealed record SharedTermInfo(
    string TermId,
    string CanonicalName,
    string DisplayName,
    ConceptType ConceptType);

public sealed record LateralImpactAcLink(
    string TargetAcId,
    string TargetAcTitle,
    string TargetRationaleId,
    IReadOnlyList<SharedTermInfo> SharedTerms);

public sealed class AcTraceabilityResult
{
    public string AcId { get; init; } = string.Empty;
    public bool Found { get; init; }
    public IReadOnlyList<string> ResolvedAtomicIds { get; init; } = [];
    public IReadOnlyList<string> ReferencedTermIds { get; init; } = [];
    public IReadOnlyList<LateralImpactAcLink> LateralImpactAcLinks { get; init; } = [];
}

public static class AcTraceabilityAnalyzer
{
    private static bool IsLateralImpactEligible(ConceptType conceptType) =>
        conceptType is ConceptType.LifecycleState
            or ConceptType.StateVariable
            or ConceptType.DomainObject
            or ConceptType.Attribute
            or ConceptType.Event;

    public static AcTraceabilityResult Analyze(KnowledgeGraphDocument document, string acId)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(acId))
        {
            return new AcTraceabilityResult { AcId = acId ?? string.Empty, Found = false };
        }

        var targetAc = document.AchievementCriteria.FirstOrDefault(
            ac => string.Equals(ac.Id, acId, StringComparison.Ordinal));
        if (targetAc is null)
        {
            return new AcTraceabilityResult { AcId = acId, Found = false };
        }

        var context = BuildContext(document);
        return AnalyzeCore(targetAc, document.AchievementCriteria, context);
    }

    public static Dictionary<string, AcTraceabilityResult> AnalyzeAll(KnowledgeGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var context = BuildContext(document);
        var results = new Dictionary<string, AcTraceabilityResult>(StringComparer.Ordinal);
        foreach (var ac in document.AchievementCriteria)
        {
            if (string.IsNullOrWhiteSpace(ac.Id) || results.ContainsKey(ac.Id))
            {
                continue;
            }

            results[ac.Id] = AnalyzeCore(ac, document.AchievementCriteria, context);
        }

        return results;
    }

    private sealed class AnalysisContext
    {
        public required Dictionary<string, ApprovedGlossaryTerm> GlossaryById { get; init; }
        public required Dictionary<string, CanonicalRequirementNode> RequirementsById { get; init; }
        public required Dictionary<string, List<CanonicalRequirementNode>> ChildrenByParentId { get; init; }
        public required Dictionary<string, UserJourney> JourneysById { get; init; }
        public Dictionary<string, HashSet<string>> ReferencedTermsCache { get; } = new(StringComparer.Ordinal);
    }

    private static AnalysisContext BuildContext(KnowledgeGraphDocument document)
    {
        var glossaryById = new Dictionary<string, ApprovedGlossaryTerm>(StringComparer.Ordinal);
        foreach (var term in document.Glossary)
        {
            if (!string.IsNullOrWhiteSpace(term.Id))
            {
                glossaryById.TryAdd(term.Id, term);
            }
        }

        var requirementsById = new Dictionary<string, CanonicalRequirementNode>(StringComparer.Ordinal);
        var childrenByParentId = new Dictionary<string, List<CanonicalRequirementNode>>(StringComparer.Ordinal);
        foreach (var req in document.Requirements)
        {
            if (!string.IsNullOrWhiteSpace(req.Id))
            {
                requirementsById.TryAdd(req.Id, req);
            }

            if (!string.IsNullOrWhiteSpace(req.ParentRequirementId))
            {
                if (!childrenByParentId.TryGetValue(req.ParentRequirementId, out var children))
                {
                    children = [];
                    childrenByParentId[req.ParentRequirementId] = children;
                }

                children.Add(req);
            }
        }

        var journeysById = new Dictionary<string, UserJourney>(StringComparer.Ordinal);
        foreach (var journey in document.Journeys)
        {
            if (!string.IsNullOrWhiteSpace(journey.Id))
            {
                journeysById.TryAdd(journey.Id, journey);
            }
        }

        return new AnalysisContext
        {
            GlossaryById = glossaryById,
            RequirementsById = requirementsById,
            ChildrenByParentId = childrenByParentId,
            JourneysById = journeysById
        };
    }

    private static AcTraceabilityResult AnalyzeCore(
        AchievementCriterion targetAc,
        List<AchievementCriterion> allCriteria,
        AnalysisContext context)
    {
        var resolvedAtomicIds = ResolveAtomicIds(targetAc, context);
        var referencedTermIds = ResolveReferencedTermIds(targetAc, resolvedAtomicIds, context);

        var eligibleSelfTerms = new HashSet<string>(
            referencedTermIds.Where(id =>
                context.GlossaryById.TryGetValue(id, out var term)
                && IsLateralImpactEligible(term.ConceptType)),
            StringComparer.Ordinal);

        var lateralLinks = new List<LateralImpactAcLink>();
        if (eligibleSelfTerms.Count > 0)
        {
            var seenOtherAcIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var otherAc in allCriteria)
            {
                if (ReferenceEquals(otherAc, targetAc)
                    || string.IsNullOrWhiteSpace(otherAc.Id)
                    || string.Equals(otherAc.Id, targetAc.Id, StringComparison.Ordinal)
                    || !seenOtherAcIds.Add(otherAc.Id))
                {
                    continue;
                }

                var otherReferencedTerms = GetOrComputeReferencedTermIds(otherAc, context);
                var sharedTerms = new List<SharedTermInfo>();

                foreach (var termId in eligibleSelfTerms)
                {
                    if (otherReferencedTerms.Contains(termId)
                        && context.GlossaryById.TryGetValue(termId, out var term))
                    {
                        sharedTerms.Add(new SharedTermInfo(
                            term.Id,
                            term.CanonicalName,
                            term.GetDisplayName(),
                            term.ConceptType));
                    }
                }

                if (sharedTerms.Count > 0)
                {
                    sharedTerms.Sort((a, b) => string.Compare(a.TermId, b.TermId, StringComparison.Ordinal));
                    lateralLinks.Add(new LateralImpactAcLink(
                        otherAc.Id,
                        otherAc.Title,
                        otherAc.RationaleId,
                        sharedTerms));
                }
            }

            lateralLinks.Sort((a, b) => string.Compare(a.TargetAcId, b.TargetAcId, StringComparison.Ordinal));
        }

        var sortedAtomics = resolvedAtomicIds.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var sortedTerms = referencedTermIds.OrderBy(id => id, StringComparer.Ordinal).ToList();

        return new AcTraceabilityResult
        {
            AcId = targetAc.Id,
            Found = true,
            ResolvedAtomicIds = sortedAtomics,
            ReferencedTermIds = sortedTerms,
            LateralImpactAcLinks = lateralLinks
        };
    }

    private static HashSet<string> GetOrComputeReferencedTermIds(AchievementCriterion ac, AnalysisContext context)
    {
        if (!string.IsNullOrWhiteSpace(ac.Id) && context.ReferencedTermsCache.TryGetValue(ac.Id, out var cached))
        {
            return cached;
        }

        var atomics = ResolveAtomicIds(ac, context);
        var terms = ResolveReferencedTermIds(ac, atomics, context);
        if (!string.IsNullOrWhiteSpace(ac.Id))
        {
            context.ReferencedTermsCache[ac.Id] = terms;
        }

        return terms;
    }

    private static HashSet<string> ResolveAtomicIds(AchievementCriterion ac, AnalysisContext context)
    {
        var atomics = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (var compositeId in ac.LinkedCompositeIds)
        {
            if (!string.IsNullOrWhiteSpace(compositeId) && visited.Add(compositeId))
            {
                queue.Enqueue(compositeId);
            }
        }

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            if (!context.ChildrenByParentId.TryGetValue(currentId, out var children))
            {
                continue;
            }

            foreach (var child in children)
            {
                if (string.IsNullOrWhiteSpace(child.Id))
                {
                    continue;
                }

                if (child.NodeKind == RequirementNodeKind.Atomic)
                {
                    atomics.Add(child.Id);
                }
                else if (visited.Add(child.Id))
                {
                    queue.Enqueue(child.Id);
                }
            }
        }

        return atomics;
    }

    private static HashSet<string> ResolveReferencedTermIds(
        AchievementCriterion ac,
        HashSet<string> resolvedAtomicIds,
        AnalysisContext context)
    {
        var termIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var atomicId in resolvedAtomicIds)
        {
            if (context.RequirementsById.TryGetValue(atomicId, out var atomic))
            {
                CollectRequirementTerms(atomic, context.GlossaryById, termIds);
            }
        }

        foreach (var journeyId in ac.LinkedJourneyIds)
        {
            if (string.IsNullOrWhiteSpace(journeyId)
                || !context.JourneysById.TryGetValue(journeyId, out var journey))
            {
                continue;
            }

            foreach (var stateTermId in journey.InitialStates.Keys)
            {
                AddIfKnownTerm(stateTermId, context.GlossaryById, termIds);
            }

            foreach (var step in journey.Steps)
            {
                if (!string.IsNullOrWhiteSpace(step.RequirementRef)
                    && context.RequirementsById.TryGetValue(step.RequirementRef, out var stepReq))
                {
                    CollectRequirementTerms(stepReq, context.GlossaryById, termIds);
                }
            }
        }

        return termIds;
    }

    private static void CollectRequirementTerms(
        CanonicalRequirementNode req,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        HashSet<string> termIds)
    {
        AddIfKnownTerm(req.SubjectSystemRef, glossaryById, termIds);
        AddIfKnownTerm(req.ActorRef, glossaryById, termIds);
        AddIfKnownTerm(req.TriggerEventRef, glossaryById, termIds);

        foreach (var pre in req.Preconditions)
        {
            AddIfKnownTerm(pre.StateTermRef, glossaryById, termIds);
        }

        if (req.Behavior is not null)
        {
            AddIfKnownTerm(req.Behavior.TargetTermRef, glossaryById, termIds);
        }
    }

    private static void AddIfKnownTerm(
        string? termId,
        Dictionary<string, ApprovedGlossaryTerm> glossaryById,
        HashSet<string> termIds)
    {
        if (!string.IsNullOrWhiteSpace(termId) && glossaryById.ContainsKey(termId))
        {
            termIds.Add(termId);
        }
    }
}
