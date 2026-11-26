using ReqPipeline.Core.Analysis;
using ReqPipeline.Core.Domain;

namespace ReqPipeline.Core.Testing;

/// <summary>
/// KnowledgeGraphDocument からテスト観点コンテナ・状態遷移マトリクス・テストケースを
/// 100% 決定論的に導出するエンジン。
/// </summary>
public static class TestSpecificationGenerator
{
    public sealed class EvaluationOutcome
    {
        public ExpectedTestOutcome ExpectedOutcome { get; init; }
        public List<string> MatchedRequirementIds { get; init; } = [];
        public string ThenTextJa { get; init; } = string.Empty;
        public string ThenTextEn { get; init; } = string.Empty;
    }

    public static TestSpecificationDocument Generate(KnowledgeGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var ctx = new GenerationContext(document);

        var allCases = new List<DerivedTestCase>();
        var containers = new List<TestViewpointContainer>();

        // 1. Unit 層: CompositeRequirement（観点コンテナ）配下の AtomicRequirement を展開
        var unitCasesByReqId = new Dictionary<string, List<DerivedTestCase>>(StringComparer.Ordinal);
        var atomicReqs = document.Requirements
            .Where(r => r.NodeKind == RequirementNodeKind.Atomic)
            .OrderBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        foreach (var req in atomicReqs)
        {
            if (req.Behavior is null || !ctx.TryGetBehaviorDef(req.Behavior.BehaviorDefRef, out _))
            {
                continue;
            }

            var derived = GenerateUnitTestCasesForRequirement(req, ctx);
            unitCasesByReqId[req.Id] = derived;
            allCases.AddRange(derived);
        }

        // CompositeRequirement ごとの観点コンテナ構築（Rationale 順 -> CompositeId 順）
        var compositeReqs = document.Requirements
            .Where(r => r.NodeKind == RequirementNodeKind.Composite)
            .OrderBy(r => r.RationaleRefs.FirstOrDefault() ?? "\uffff", StringComparer.Ordinal)
            .ThenBy(r => r.Id, StringComparer.Ordinal)
            .ToList();

        var assignedAtomicIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var comp in compositeReqs)
        {
            var childAtomics = atomicReqs
                .Where(a => string.Equals(a.ParentRequirementId, comp.Id, StringComparison.Ordinal))
                .ToList();

            var compCases = new List<DerivedTestCase>();
            foreach (var child in childAtomics)
            {
                assignedAtomicIds.Add(child.Id);
                if (unitCasesByReqId.TryGetValue(child.Id, out var childCases))
                {
                    compCases.AddRange(childCases);
                }
            }

            var childIdSet = childAtomics.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            childIdSet.Add(comp.Id);

            var escapes = document.EscapeRecords
                .Where(e => e.Status != EscapeRecordStatus.REJECTED && childIdSet.Contains(e.RequirementId))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var ratId = comp.RationaleRefs.FirstOrDefault();
            var ratTitle = ratId is not null && ctx.RationaleById.TryGetValue(ratId, out var rat)
                ? rat.Title
                : null;

            containers.Add(new TestViewpointContainer
            {
                Id = comp.Id,
                Kind = TestViewpointKind.RequirementTree,
                RationaleId = ratId,
                RationaleTitle = ratTitle,
                Title = comp.Title,
                TitleEn = comp.Title,
                Description = comp.Summary,
                SourceRef = comp.Id,
                TestCases = SortTestCases(compCases),
                EscapeRecords = escapes
            });
        }

        // 親 Composite を持たない孤立 Atomic 要求があれば個別コンテナとして収容
        foreach (var orphan in atomicReqs.Where(a => !assignedAtomicIds.Contains(a.Id)))
        {
            unitCasesByReqId.TryGetValue(orphan.Id, out var orphanCases);
            var escapes = document.EscapeRecords
                .Where(e => e.Status != EscapeRecordStatus.REJECTED
                            && string.Equals(e.RequirementId, orphan.Id, StringComparison.Ordinal))
                .OrderBy(e => e.Id, StringComparer.Ordinal)
                .ToList();

            var ratId = orphan.RationaleRefs.FirstOrDefault();
            var ratTitle = ratId is not null && ctx.RationaleById.TryGetValue(ratId, out var rat)
                ? rat.Title
                : null;

            containers.Add(new TestViewpointContainer
            {
                Id = orphan.Id,
                Kind = TestViewpointKind.RequirementTree,
                RationaleId = ratId,
                RationaleTitle = ratTitle,
                Title = orphan.Title,
                TitleEn = orphan.Title,
                Description = orphan.Summary,
                SourceRef = orphan.Id,
                TestCases = SortTestCases(orphanCases ?? []),
                EscapeRecords = escapes
            });
        }

        // 2. Component 層: LifecycleState ごとの 2 次元状態遷移マトリクス
        var fsmModels = FsmGraphBuilder.Build(document);
        var matrices = new List<StateTransitionMatrix>();

        foreach (var fsm in fsmModels)
        {
            if (!ctx.GlossaryById.TryGetValue(fsm.StateTermId, out var lcTerm))
            {
                continue;
            }

            var matrix = BuildStateTransitionMatrix(lcTerm, fsm, ctx);
            matrices.Add(matrix);

            var matrixCases = matrix.Cells.SelectMany(c => c.TestCases).ToList();
            allCases.AddRange(matrixCases);

            containers.Add(new TestViewpointContainer
            {
                Id = lcTerm.Id,
                Kind = TestViewpointKind.FsmMatrix,
                Title = $"{lcTerm.GetDisplayName()} 状態遷移マトリクス",
                TitleEn = $"{lcTerm.CanonicalName} State Transition Matrix",
                Description = lcTerm.Description,
                SourceRef = lcTerm.Id,
                TestCases = SortTestCases(matrixCases),
                UndefinedCellCount = matrix.UndefinedCellCount
            });
        }

        // 3. E2E 層: UserJourney ごとの通し検証ケース
        foreach (var journey in document.Journeys.OrderBy(j => j.Id, StringComparer.Ordinal))
        {
            var simResult = JourneySimulator.Simulate(document, journey);
            var e2eCase = BuildJourneyTestCase(journey, simResult, ctx);
            allCases.Add(e2eCase);

            containers.Add(new TestViewpointContainer
            {
                Id = journey.Id,
                Kind = TestViewpointKind.Journey,
                Title = journey.Title,
                TitleEn = journey.Title,
                Description = journey.Description,
                SourceRef = journey.Id,
                TestCases = [e2eCase],
                HasBlockedSteps = simResult.BlockedStepCount > 0
            });
        }

        // 固定ソート順適用
        var sortedAllCases = SortTestCases(allCases);

        // サマリー集計
        var nonEscapeAtomics = atomicReqs.Where(r => r.Behavior is not null).ToList();
        var coveredAtomics = nonEscapeAtomics.Count(r =>
            unitCasesByReqId.TryGetValue(r.Id, out var list) && list.Count > 0);
        var coveragePercent = nonEscapeAtomics.Count == 0
            ? 100.0
            : Math.Round(100.0 * coveredAtomics / nonEscapeAtomics.Count, 1);

        var summary = new TestSpecificationSummary
        {
            TotalViewpointContainers = containers.Count,
            TotalTestCases = sortedAllCases.Count,
            UnitTestCaseCount = sortedAllCases.Count(c => c.TestLevel == TestLevel.Unit),
            ComponentTestCaseCount = sortedAllCases.Count(c => c.TestLevel == TestLevel.Component),
            E2ETestCaseCount = sortedAllCases.Count(c => c.TestLevel == TestLevel.E2E),
            TotalNonEscapeAtomicRequirements = nonEscapeAtomics.Count,
            CoveredAtomicRequirements = coveredAtomics,
            AtomicRequirementCoveragePercent = coveragePercent,
            TotalUndefinedCells = matrices.Sum(m => m.UndefinedCellCount),
            EscapeManualReviewCount = document.EscapeRecords.Count(e => e.Status != EscapeRecordStatus.REJECTED)
        };

        return new TestSpecificationDocument
        {
            DocumentId = document.DocumentId,
            Title = document.Title,
            Description = document.Description,
            Summary = summary,
            ViewpointContainers = containers,
            TransitionMatrices = matrices,
            AllTestCases = sortedAllCases
        };
    }

    /// <summary>
    /// 指定された (StateDictionary, TriggerEventRef) に対してドキュメント内の全 Atomic 要求を決定論的に再評価する。
    /// </summary>
    public static EvaluationOutcome EvaluateAllRequirements(
        KnowledgeGraphDocument document,
        IReadOnlyDictionary<string, string> stateDict,
        string? triggerEventRef)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(stateDict);

        var ctx = new GenerationContext(document);
        return EvaluateAllRequirementsInternal(stateDict, triggerEventRef, ctx);
    }

    private static List<DerivedTestCase> GenerateUnitTestCasesForRequirement(
        CanonicalRequirementNode req,
        GenerationContext ctx)
    {
        var behavior = req.Behavior!;
        var behaviorDef = ctx.BehaviorById[behavior.BehaviorDefRef];
        var rationaleId = ctx.ResolveRationaleId(req);
        var compositeId = req.ParentRequirementId;
        var (viewpointTitleJa, viewpointTitleEn) = ctx.ResolveViewpointTitle(req);

        if (behaviorDef.Effect == SemanticEffect.Prohibit)
        {
            return GenerateProhibitUnitCases(req, behaviorDef, rationaleId, compositeId, viewpointTitleJa, viewpointTitleEn, ctx);
        }

        return GenerateNormalUnitCases(req, behaviorDef, rationaleId, compositeId, viewpointTitleJa, viewpointTitleEn, ctx);
    }

    private static List<DerivedTestCase> GenerateNormalUnitCases(
        CanonicalRequirementNode req,
        BehaviorDefinition behaviorDef,
        string? rationaleId,
        string? compositeId,
        string viewpointTitleJa,
        string viewpointTitleEn,
        GenerationContext ctx)
    {
        var results = new List<DerivedTestCase>();
        var behavior = req.Behavior!;

        // 成立する条件辞書と反転候補の収集（StateTermRef ごとに重複排除）
        var baseStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var orderedConditions = new List<(string TermId, List<string> NegatedValues)>();
        var seenTerms = new HashSet<string>(StringComparer.Ordinal);

        if (behaviorDef.Effect == SemanticEffect.MutateState
            && !string.IsNullOrWhiteSpace(behavior.FromValue)
            && !string.IsNullOrWhiteSpace(behavior.TargetTermRef))
        {
            baseStates[behavior.TargetTermRef] = behavior.FromValue;
            seenTerms.Add(behavior.TargetTermRef);
            var negValues = GetNegatedValuesForExactValue(behavior.TargetTermRef, behavior.FromValue, ctx);
            orderedConditions.Add((behavior.TargetTermRef, negValues));
        }

        foreach (var clause in req.Preconditions)
        {
            if (string.IsNullOrWhiteSpace(clause.StateTermRef))
            {
                continue;
            }

            if (!seenTerms.Add(clause.StateTermRef))
            {
                // FromValue と同じ Term が Preconditions に重複定義されている場合はスキップ
                continue;
            }

            var satValue = GetSatisfyingValue(clause, ctx);
            if (satValue is not null)
            {
                baseStates[clause.StateTermRef] = satValue;
            }

            var negValues = GetNegatedValues(clause, ctx);
            orderedConditions.Add((clause.StateTermRef, negValues));
        }

        var givenEntries = BuildConditionEntries(baseStates, ctx);
        var whenSteps = BuildSingleWhenStep(req.ActorRef, req.TriggerEventRef, req.Id, ctx);
        var (posOutcome, posThenJa, posThenEn) = BuildPositiveExpectedResult(req, behaviorDef, ctx);

        // 1. POS (正常発火)
        results.Add(new DerivedTestCase
        {
            TestCaseId = $"TC-{req.Id}-POS-01",
            TestLevel = TestLevel.Unit,
            Technique = TestTechnique.ConditionNormal,
            RationaleId = rationaleId,
            CompositeId = compositeId,
            SourceRef = req.Id,
            ViewpointTitle = viewpointTitleJa,
            ViewpointTitleEn = viewpointTitleEn,
            Title = $"{req.Title}（正常発火）",
            TitleEn = $"{req.Title} (Normal Trigger)",
            GivenConditions = givenEntries,
            WhenSteps = whenSteps,
            ThenTextJa = posThenJa,
            ThenTextEn = posThenEn,
            ExpectedOutcome = posOutcome,
            Tags = BuildUnitTags(rationaleId, compositeId, req.Id, TestTechnique.ConditionNormal, posOutcome)
        });

        // 2. NEG (1条件反転: 各条件の非一致値を定義順に全件展開)
        var negSeq = 1;
        foreach (var (termId, negValues) in orderedConditions)
        {
            foreach (var negVal in negValues)
            {
                var mutatedStates = new Dictionary<string, string>(baseStates, StringComparer.Ordinal)
                {
                    [termId] = negVal
                };

                var eval = EvaluateAllRequirementsInternal(mutatedStates, req.TriggerEventRef, ctx);
                var termNameJa = ctx.ResolveDisplayName(termId);
                var termNameEn = ctx.ResolveCanonicalName(termId);
                var negValJa = ctx.ResolveValueLabelJa(termId, negVal);

                results.Add(new DerivedTestCase
                {
                    TestCaseId = $"TC-{req.Id}-NEG-{negSeq:D2}",
                    TestLevel = TestLevel.Unit,
                    Technique = TestTechnique.ConditionNegation,
                    RationaleId = rationaleId,
                    CompositeId = compositeId,
                    SourceRef = req.Id,
                    ViewpointTitle = viewpointTitleJa,
                    ViewpointTitleEn = viewpointTitleEn,
                    Title = $"{req.Title}（条件反転: {termNameJa}={negValJa}）",
                    TitleEn = $"{req.Title} (Negation: {termNameEn}={negVal})",
                    GivenConditions = BuildConditionEntries(mutatedStates, ctx),
                    WhenSteps = whenSteps,
                    ThenTextJa = eval.ThenTextJa,
                    ThenTextEn = eval.ThenTextEn,
                    ExpectedOutcome = eval.ExpectedOutcome,
                    Tags = BuildUnitTags(rationaleId, compositeId, req.Id, TestTechnique.ConditionNegation, eval.ExpectedOutcome)
                });
                negSeq++;
            }
        }

        return results;
    }

    private static List<DerivedTestCase> GenerateProhibitUnitCases(
        CanonicalRequirementNode req,
        BehaviorDefinition behaviorDef,
        string? rationaleId,
        string? compositeId,
        string viewpointTitleJa,
        string viewpointTitleEn,
        GenerationContext ctx)
    {
        var results = new List<DerivedTestCase>();
        var behavior = req.Behavior!;

        // StateDriven等でトリガー未指定の場合、対応する通常要求からトリガー・アクター・FromValue を補完
        var effectiveTrigger = req.TriggerEventRef;
        var effectiveActor = req.ActorRef;
        var pairedNormalReqs = ctx.FindMatchingNormalRequirementsForProhibit(req);

        if (string.IsNullOrWhiteSpace(effectiveTrigger))
        {
            var firstPaired = pairedNormalReqs.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n.TriggerEventRef));
            if (firstPaired is not null)
            {
                effectiveTrigger = firstPaired.TriggerEventRef;
                effectiveActor ??= firstPaired.ActorRef;
            }
        }

        var baseStates = new Dictionary<string, string>(StringComparer.Ordinal);
        var prohibitCondList = new List<(string TermId, List<string> NegatedValues)>();
        var seenTerms = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(behavior.FromValue) && !string.IsNullOrWhiteSpace(behavior.TargetTermRef))
        {
            baseStates[behavior.TargetTermRef] = behavior.FromValue;
            seenTerms.Add(behavior.TargetTermRef);
            prohibitCondList.Add((behavior.TargetTermRef, GetNegatedValuesForExactValue(behavior.TargetTermRef, behavior.FromValue, ctx)));
        }

        foreach (var clause in req.Preconditions)
        {
            if (string.IsNullOrWhiteSpace(clause.StateTermRef) || !seenTerms.Add(clause.StateTermRef))
            {
                continue;
            }

            var satValue = GetSatisfyingValue(clause, ctx);
            if (satValue is not null)
            {
                baseStates[clause.StateTermRef] = satValue;
            }

            prohibitCondList.Add((clause.StateTermRef, GetNegatedValues(clause, ctx)));
        }

        // 対応通常要求の FromValue 等（禁止条件と競合しないもの）を補完し、UNB 時の対照検証を正確に行う
        var firstNormal = pairedNormalReqs.FirstOrDefault();
        if (firstNormal?.Behavior is not null
            && !string.IsNullOrWhiteSpace(firstNormal.Behavior.FromValue)
            && !string.IsNullOrWhiteSpace(firstNormal.Behavior.TargetTermRef)
            && !baseStates.ContainsKey(firstNormal.Behavior.TargetTermRef))
        {
            baseStates[firstNormal.Behavior.TargetTermRef] = firstNormal.Behavior.FromValue;
        }

        var whenSteps = BuildSingleWhenStep(effectiveActor, effectiveTrigger, req.Id, ctx);

        // 1. BLK (禁止ブロック)
        results.Add(new DerivedTestCase
        {
            TestCaseId = $"TC-{req.Id}-BLK-01",
            TestLevel = TestLevel.Unit,
            Technique = TestTechnique.SafetyInterlock,
            RationaleId = rationaleId,
            CompositeId = compositeId,
            SourceRef = req.Id,
            ViewpointTitle = viewpointTitleJa,
            ViewpointTitleEn = viewpointTitleEn,
            Title = $"{req.Title}（禁止ブロック）",
            TitleEn = $"{req.Title} (Prohibit Block)",
            GivenConditions = BuildConditionEntries(baseStates, ctx),
            WhenSteps = whenSteps,
            ThenTextJa = $"\"{req.Id}\" により操作がブロックされ、状態が維持されること",
            ThenTextEn = $"Operation is blocked by \"{req.Id}\" and state remains unchanged",
            ExpectedOutcome = ExpectedTestOutcome.Blocked,
            Tags = BuildUnitTags(rationaleId, compositeId, req.Id, TestTechnique.SafetyInterlock, ExpectedTestOutcome.Blocked)
        });

        // 2. UNB (禁止解除: 禁止条件を1つずつ崩して全要求再評価)
        var unbSeq = 1;
        foreach (var (termId, negValues) in prohibitCondList)
        {
            foreach (var negVal in negValues)
            {
                var unblockedStates = new Dictionary<string, string>(baseStates, StringComparer.Ordinal)
                {
                    [termId] = negVal
                };

                var eval = EvaluateAllRequirementsInternal(unblockedStates, effectiveTrigger, ctx);
                var termNameJa = ctx.ResolveDisplayName(termId);
                var termNameEn = ctx.ResolveCanonicalName(termId);
                var negValJa = ctx.ResolveValueLabelJa(termId, negVal);

                results.Add(new DerivedTestCase
                {
                    TestCaseId = $"TC-{req.Id}-UNB-{unbSeq:D2}",
                    TestLevel = TestLevel.Unit,
                    Technique = TestTechnique.SafetyInterlock,
                    RationaleId = rationaleId,
                    CompositeId = compositeId,
                    SourceRef = req.Id,
                    ViewpointTitle = viewpointTitleJa,
                    ViewpointTitleEn = viewpointTitleEn,
                    Title = $"{req.Title}（禁止解除: {termNameJa}={negValJa}）",
                    TitleEn = $"{req.Title} (Unblock: {termNameEn}={negVal})",
                    GivenConditions = BuildConditionEntries(unblockedStates, ctx),
                    WhenSteps = whenSteps,
                    ThenTextJa = eval.ThenTextJa,
                    ThenTextEn = eval.ThenTextEn,
                    ExpectedOutcome = eval.ExpectedOutcome,
                    Tags = BuildUnitTags(rationaleId, compositeId, req.Id, TestTechnique.SafetyInterlock, eval.ExpectedOutcome)
                });
                unbSeq++;
            }
        }

        return results;
    }

    private static StateTransitionMatrix BuildStateTransitionMatrix(
        ApprovedGlossaryTerm lcTerm,
        FsmModel fsm,
        GenerationContext ctx)
    {
        var states = fsm.States.Select(s => s.Value).ToList();
        var stateLabels = fsm.States.ToDictionary(s => s.Value, s => s.Label, StringComparer.Ordinal);

        // 関連要求の抽出: 当該 LifecycleState を TargetTermRef または Preconditions に参照する Atomic 要求
        var relatedReqs = ctx.AtomicRequirements
            .Where(r => r.Behavior is not null
                        && (string.Equals(r.Behavior.TargetTermRef, lcTerm.Id, StringComparison.Ordinal)
                            || r.Preconditions.Any(p => string.Equals(p.StateTermRef, lcTerm.Id, StringComparison.Ordinal))))
            .ToList();

        // 関連イベントの絞り込み
        var relatedEventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var req in relatedReqs)
        {
            if (!string.IsNullOrWhiteSpace(req.TriggerEventRef))
            {
                relatedEventIds.Add(req.TriggerEventRef);
            }
            else if (ctx.IsProhibit(req))
            {
                foreach (var trig in ctx.GetEffectiveTriggersForProhibit(req))
                {
                    relatedEventIds.Add(trig);
                }
            }
        }

        // Glossary 定義順を維持して列リスト作成（未登録 ID は末尾に Ordinal 順）
        var eventColumns = new List<MatrixEventColumn>();
        var addedEventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var term in ctx.Document.Glossary)
        {
            if (relatedEventIds.Contains(term.Id) && addedEventIds.Add(term.Id))
            {
                eventColumns.Add(new MatrixEventColumn
                {
                    EventTermId = term.Id,
                    CanonicalName = term.CanonicalName,
                    DisplayName = term.GetDisplayName()
                });
            }
        }

        foreach (var evtId in relatedEventIds.Where(id => !addedEventIds.Contains(id)).OrderBy(id => id, StringComparer.Ordinal))
        {
            eventColumns.Add(new MatrixEventColumn
            {
                EventTermId = evtId,
                CanonicalName = evtId,
                DisplayName = evtId
            });
        }

        var cells = new List<MatrixCell>(states.Count * eventColumns.Count);
        var shortTermId = ShortenTermId(lcTerm.Id);

        foreach (var state in states)
        {
            var stateLabel = stateLabels[state];

            foreach (var eventCol in eventColumns)
            {
                var cellReqs = relatedReqs
                    .Where(r => MatchesMatrixCell(r, lcTerm.Id, state, eventCol.EventTermId, ctx))
                    .OrderBy(r => r.Id, StringComparer.Ordinal)
                    .ToList();

                var normalReqs = cellReqs.Where(r => !ctx.IsProhibit(r)).ToList();
                var prohibitReqs = cellReqs.Where(r => ctx.IsProhibit(r)).ToList();
                var hasProhibit = prohibitReqs.Count > 0;

                MatrixCellKind classification;
                if (cellReqs.Count == 0)
                {
                    classification = MatrixCellKind.UndefinedNoOp;
                }
                else if (normalReqs.Count == 0)
                {
                    classification = MatrixCellKind.ExplicitProhibit;
                }
                else
                {
                    var hasExternalGuard = cellReqs.Any(r =>
                        r.Preconditions.Any(p => !string.Equals(p.StateTermRef, lcTerm.Id, StringComparison.Ordinal)));
                    var hasNonMutate = normalReqs.Any(r =>
                        ctx.BehaviorById[r.Behavior!.BehaviorDefRef].Effect != SemanticEffect.MutateState);

                    classification = (hasExternalGuard || hasNonMutate || hasProhibit)
                        ? MatrixCellKind.ConditionalTransition
                        : MatrixCellKind.ValidTransition;
                }

                var targetStates = normalReqs
                    .Where(r => ctx.BehaviorById[r.Behavior!.BehaviorDefRef].Effect == SemanticEffect.MutateState
                                && string.Equals(r.Behavior.TargetTermRef, lcTerm.Id, StringComparison.Ordinal)
                                && !string.IsNullOrWhiteSpace(r.Behavior.ToValue))
                    .Select(r => r.Behavior!.ToValue!)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                var targetStateLabels = targetStates
                    .Select(v => ctx.ResolveValueLabelJa(lcTerm.Id, v))
                    .ToList();

                var cellCases = GenerateMatrixCellTestCases(
                    lcTerm,
                    shortTermId,
                    state,
                    stateLabel,
                    eventCol,
                    classification,
                    hasProhibit,
                    normalReqs,
                    prohibitReqs,
                    ctx);

                cells.Add(new MatrixCell
                {
                    FromState = state,
                    FromStateLabel = stateLabel,
                    EventTermId = eventCol.EventTermId,
                    EventCanonicalName = eventCol.CanonicalName,
                    EventDisplayName = eventCol.DisplayName,
                    Classification = classification,
                    HasProhibit = hasProhibit,
                    TargetStates = targetStates,
                    TargetStateLabels = targetStateLabels,
                    RelatedRequirementIds = cellReqs.Select(r => r.Id).ToList(),
                    TestCases = cellCases
                });
            }
        }

        return new StateTransitionMatrix
        {
            LifecycleTermId = lcTerm.Id,
            LifecycleLabel = lcTerm.GetDisplayName(),
            LifecycleCanonicalLabel = lcTerm.CanonicalName,
            States = states,
            StateLabels = stateLabels,
            Events = eventColumns,
            Cells = cells
        };
    }

    private static List<DerivedTestCase> GenerateMatrixCellTestCases(
        ApprovedGlossaryTerm lcTerm,
        string shortTermId,
        string fromState,
        string fromStateLabel,
        MatrixEventColumn eventCol,
        MatrixCellKind classification,
        bool hasProhibit,
        List<CanonicalRequirementNode> normalReqs,
        List<CanonicalRequirementNode> prohibitReqs,
        GenerationContext ctx)
    {
        var baseId = $"TC-FSM-{shortTermId}-{fromState}-{ShortenTermId(eventCol.EventTermId)}";
        var viewpointJa = $"{lcTerm.GetDisplayName()} 状態遷移マトリクス";
        var viewpointEn = $"{lcTerm.CanonicalName} State Transition Matrix";
        var actorRef = normalReqs.Concat(prohibitReqs)
            .Select(r => r.ActorRef)
            .FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));

        // 1. 純粋な空白セル (UndefinedNoOp)
        if (classification == MatrixCellKind.UndefinedNoOp)
        {
            var states = new Dictionary<string, string>(StringComparer.Ordinal) { [lcTerm.Id] = fromState };
            return
            [
                new DerivedTestCase
                {
                    TestCaseId = baseId,
                    TestLevel = TestLevel.Component,
                    Technique = TestTechnique.InvalidTransition,
                    SourceRef = lcTerm.Id,
                    ViewpointTitle = viewpointJa,
                    ViewpointTitleEn = viewpointEn,
                    Title = $"[{fromStateLabel}] × [{eventCol.DisplayName}] 未定義遷移確認",
                    TitleEn = $"[{fromState}] x [{eventCol.CanonicalName}] Undefined Transition",
                    GivenConditions = BuildConditionEntries(states, ctx),
                    WhenSteps = BuildSingleWhenStep(actorRef, eventCol.EventTermId, null, ctx),
                    ThenTextJa = $"該当する要求が存在しないため、\"{lcTerm.GetDisplayName()}\" は \"{fromStateLabel}\" のまま変化しないこと",
                    ThenTextEn = $"No matching requirement exists; \"{lcTerm.CanonicalName}\" remains \"{fromState}\"",
                    ExpectedOutcome = ExpectedTestOutcome.NoOp,
                    ReviewNoteJa = "未定義セル：意図的な無視（No-op）か仕様漏れか要確認",
                    ReviewNoteEn = "Undefined cell: verify whether intentional no-op or missing specification",
                    Tags = BuildFsmTags(lcTerm.Id, TestTechnique.InvalidTransition, ExpectedTestOutcome.NoOp, [])
                }
            ];
        }

        // 2. 無条件の有効遷移 (ValidTransition)
        if (classification == MatrixCellKind.ValidTransition)
        {
            var states = new Dictionary<string, string>(StringComparer.Ordinal) { [lcTerm.Id] = fromState };
            var cases = new List<DerivedTestCase>();
            for (var i = 0; i < normalReqs.Count; i++)
            {
                var req = normalReqs[i];
                var behDef = ctx.BehaviorById[req.Behavior!.BehaviorDefRef];
                var (outcome, thenJa, thenEn) = BuildPositiveExpectedResult(req, behDef, ctx);
                var caseId = normalReqs.Count == 1 ? baseId : $"{baseId}-{i + 1:D2}";

                cases.Add(new DerivedTestCase
                {
                    TestCaseId = caseId,
                    TestLevel = TestLevel.Component,
                    Technique = TestTechnique.ValidTransition,
                    RationaleId = ctx.ResolveRationaleId(req),
                    CompositeId = req.ParentRequirementId,
                    SourceRef = lcTerm.Id,
                    ViewpointTitle = viewpointJa,
                    ViewpointTitleEn = viewpointEn,
                    Title = $"[{fromStateLabel}] × [{eventCol.DisplayName}] → {ctx.ResolveValueLabelJa(lcTerm.Id, req.Behavior.ToValue ?? string.Empty)}",
                    TitleEn = $"[{fromState}] x [{eventCol.CanonicalName}] -> {req.Behavior.ToValue}",
                    GivenConditions = BuildConditionEntries(states, ctx),
                    WhenSteps = BuildSingleWhenStep(req.ActorRef ?? actorRef, eventCol.EventTermId, req.Id, ctx),
                    ThenTextJa = thenJa,
                    ThenTextEn = thenEn,
                    ExpectedOutcome = outcome,
                    Tags = BuildFsmTags(lcTerm.Id, TestTechnique.ValidTransition, outcome, [req.Id])
                });
            }
            return cases;
        }

        // 3. ExplicitProhibit または ConditionalTransition: ガード変数の成立／不成立パターンを展開し全要求再評価
        var allCellReqs = normalReqs.Concat(prohibitReqs).ToList();
        var statePatterns = new List<Dictionary<string, string>>();
        var seenPatternKeys = new HashSet<string>(StringComparer.Ordinal);

        void AddPatternIfNew(Dictionary<string, string> pattern)
        {
            var key = string.Join(";", pattern.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));
            if (seenPatternKeys.Add(key))
            {
                statePatterns.Add(pattern);
            }
        }

        foreach (var req in allCellReqs)
        {
            var satPattern = new Dictionary<string, string>(StringComparer.Ordinal) { [lcTerm.Id] = fromState };
            var externalClauses = req.Preconditions
                .Where(p => !string.Equals(p.StateTermRef, lcTerm.Id, StringComparison.Ordinal))
                .ToList();

            foreach (var clause in externalClauses)
            {
                var satVal = GetSatisfyingValue(clause, ctx);
                if (satVal is not null)
                {
                    satPattern[clause.StateTermRef] = satVal;
                }
            }

            AddPatternIfNew(satPattern);

            // 外部ガードを1つずつ崩したパターンも追加
            foreach (var clause in externalClauses)
            {
                foreach (var negVal in GetNegatedValues(clause, ctx))
                {
                    var negPattern = new Dictionary<string, string>(satPattern, StringComparer.Ordinal)
                    {
                        [clause.StateTermRef] = negVal
                    };
                    AddPatternIfNew(negPattern);
                }
            }
        }

        var rawCases = new List<(Dictionary<string, string> StateDict, EvaluationOutcome Eval, TestTechnique Technique, List<string> MatchedIds)>();
        foreach (var pattern in statePatterns)
        {
            var eval = EvaluateAllRequirementsInternal(pattern, eventCol.EventTermId, ctx);
            var tech = eval.ExpectedOutcome == ExpectedTestOutcome.Blocked
                ? TestTechnique.ExplicitProhibit
                : (classification == MatrixCellKind.ExplicitProhibit
                    ? TestTechnique.ExplicitProhibit
                    : TestTechnique.ConditionalTransition);

            // 単一通常要求ヒット時は Positive 表現（StateChanged / ActionEmitted）に正規化
            var normalizedEval = NormalizeMatrixEvaluation(eval, ctx);
            rawCases.Add((pattern, normalizedEval, tech, eval.MatchedRequirementIds));
        }

        var resultCases = new List<DerivedTestCase>(rawCases.Count);
        for (var i = 0; i < rawCases.Count; i++)
        {
            var (pattern, eval, tech, matchedIds) = rawCases[i];
            var caseId = rawCases.Count == 1 ? baseId : $"{baseId}-{i + 1:D2}";
            var extraCondJa = string.Join(", ", pattern
                .Where(kv => !string.Equals(kv.Key, lcTerm.Id, StringComparison.Ordinal))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{ctx.ResolveDisplayName(kv.Key)}={ctx.ResolveValueLabelJa(kv.Key, kv.Value)}"));
            var extraCondEn = string.Join(", ", pattern
                .Where(kv => !string.Equals(kv.Key, lcTerm.Id, StringComparison.Ordinal))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{ctx.ResolveCanonicalName(kv.Key)}={kv.Value}"));

            var titleSuffixJa = string.IsNullOrEmpty(extraCondJa) ? string.Empty : $"（{extraCondJa}）";
            var titleSuffixEn = string.IsNullOrEmpty(extraCondEn) ? string.Empty : $" ({extraCondEn})";

            var primaryReq = matchedIds.Count > 0 && ctx.RequirementById.TryGetValue(matchedIds[0], out var r)
                ? r
                : null;

            resultCases.Add(new DerivedTestCase
            {
                TestCaseId = caseId,
                TestLevel = TestLevel.Component,
                Technique = tech,
                RationaleId = primaryReq is not null ? ctx.ResolveRationaleId(primaryReq) : null,
                CompositeId = primaryReq?.ParentRequirementId,
                SourceRef = lcTerm.Id,
                ViewpointTitle = viewpointJa,
                ViewpointTitleEn = viewpointEn,
                Title = $"[{fromStateLabel}] × [{eventCol.DisplayName}]{titleSuffixJa}",
                TitleEn = $"[{fromState}] x [{eventCol.CanonicalName}]{titleSuffixEn}",
                GivenConditions = BuildConditionEntries(pattern, ctx),
                WhenSteps = BuildSingleWhenStep(primaryReq?.ActorRef ?? actorRef, eventCol.EventTermId, primaryReq?.Id, ctx),
                ThenTextJa = eval.ThenTextJa,
                ThenTextEn = eval.ThenTextEn,
                ExpectedOutcome = eval.ExpectedOutcome,
                Tags = BuildFsmTags(lcTerm.Id, tech, eval.ExpectedOutcome, matchedIds)
            });
        }

        return resultCases;
    }

    private static EvaluationOutcome NormalizeMatrixEvaluation(EvaluationOutcome eval, GenerationContext ctx)
    {
        if (eval.ExpectedOutcome == ExpectedTestOutcome.AlternativeFired && eval.MatchedRequirementIds.Count == 1)
        {
            var req = ctx.RequirementById[eval.MatchedRequirementIds[0]];
            var behDef = ctx.BehaviorById[req.Behavior!.BehaviorDefRef];
            var (outcome, thenJa, thenEn) = BuildPositiveExpectedResult(req, behDef, ctx);
            return new EvaluationOutcome
            {
                ExpectedOutcome = outcome,
                MatchedRequirementIds = eval.MatchedRequirementIds,
                ThenTextJa = thenJa,
                ThenTextEn = thenEn
            };
        }

        return eval;
    }

    private static DerivedTestCase BuildJourneyTestCase(
        UserJourney journey,
        JourneySimulationResult sim,
        GenerationContext ctx)
    {
        var givenEntries = BuildConditionEntries(sim.InitialStates, ctx);
        var whenSteps = new List<TestActionEntry>(sim.Steps.Count);

        for (var i = 0; i < sim.Steps.Count; i++)
        {
            var step = sim.Steps[i];
            ctx.RequirementById.TryGetValue(step.RequirementRef, out var req);
            var actorRef = req?.ActorRef;
            var eventRef = req?.TriggerEventRef;

            whenSteps.Add(new TestActionEntry
            {
                StepNumber = i + 1,
                ActorTermId = actorRef,
                ActorLabelJa = actorRef is not null ? ctx.ResolveDisplayName(actorRef) : null,
                ActorLabelEn = actorRef is not null ? ctx.ResolveCanonicalName(actorRef) : null,
                EventTermId = eventRef,
                EventLabelJa = eventRef is not null ? ctx.ResolveDisplayName(eventRef) : null,
                EventLabelEn = eventRef is not null ? ctx.ResolveCanonicalName(eventRef) : null,
                ActionTextJa = !string.IsNullOrWhiteSpace(step.UserAction)
                    ? step.UserAction
                    : (step.RequirementTitle ?? step.RequirementRef),
                ActionTextEn = !string.IsNullOrWhiteSpace(step.UserAction)
                    ? step.UserAction
                    : (step.RequirementTitle ?? step.RequirementRef),
                RequirementRef = step.RequirementRef
            });
        }

        var stepLinesJa = new List<string>(sim.Steps.Count + 1);
        var stepLinesEn = new List<string>(sim.Steps.Count + 1);

        for (var i = 0; i < sim.Steps.Count; i++)
        {
            var s = sim.Steps[i];
            var statusJa = s.Status switch
            {
                JourneyStepStatus.Passed => "成功",
                JourneyStepStatus.Blocked => "ブロック",
                _ => $"失敗({s.Status})"
            };

            var detailJa = FormatJourneyStepSummary(s, ctx, isJa: true);
            var detailEn = FormatJourneyStepSummary(s, ctx, isJa: false);

            stepLinesJa.Add($"{i + 1}. ({s.RequirementRef}) {statusJa} [{detailJa}]");
            stepLinesEn.Add($"{i + 1}. ({s.RequirementRef}) {s.Status} [{detailEn}]");
        }

        var orderedFinalStates = sim.FinalStates
            .OrderBy(kv => ctx.GetGlossaryOrder(kv.Key))
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        var finalStatesJa = string.Join(", ", orderedFinalStates
            .Select(kv => $"\"{ctx.ResolveDisplayName(kv.Key)}\"=\"{ctx.ResolveValueLabelJa(kv.Key, kv.Value)}\""));

        var finalStatesEn = string.Join(", ", orderedFinalStates
            .Select(kv => $"\"{ctx.ResolveCanonicalName(kv.Key)}\"=\"{kv.Value}\""));

        stepLinesJa.Add($"最終状態: {finalStatesJa}");
        stepLinesEn.Add($"Final states: {finalStatesEn}");

        var thenJa = string.Join("\n", stepLinesJa);
        var thenEn = string.Join("\n", stepLinesEn);

        ExpectedTestOutcome outcome;
        if (sim.BlockedStepCount > 0)
        {
            outcome = ExpectedTestOutcome.Blocked;
        }
        else if (sim.Steps.Count == 0)
        {
            outcome = ExpectedTestOutcome.NoOp;
        }
        else
        {
            var stateChanged = !DictionariesEqual(sim.InitialStates, sim.FinalStates)
                || sim.Steps.Any(s => !DictionariesEqual(s.StateBefore, s.StateAfter));
            outcome = stateChanged ? ExpectedTestOutcome.StateChanged : ExpectedTestOutcome.ActionEmitted;
        }

        string? reviewNoteJa = null;
        string? reviewNoteEn = null;
        if (sim.FailedStepCount > 0)
        {
            reviewNoteJa = $"ジャーニーシミュレーションで {sim.FailedStepCount} 件の失敗ステップがあります。前提状態または要求定義を確認してください。";
            reviewNoteEn = $"Journey simulation reported {sim.FailedStepCount} failed step(s). Verify initial states or requirement definitions.";
        }

        var tags = new List<string>
        {
            $"@{journey.Id}",
            "@level:e2e",
            "@technique:journey-trace"
        };
        if (sim.BlockedStepCount > 0)
        {
            tags.Add("@safety");
        }

        return new DerivedTestCase
        {
            TestCaseId = $"TC-{journey.Id}",
            TestLevel = TestLevel.E2E,
            Technique = TestTechnique.JourneyTrace,
            SourceRef = journey.Id,
            ViewpointTitle = journey.Title,
            ViewpointTitleEn = journey.Title,
            Title = $"{journey.Title}（E2E 通し検証）",
            TitleEn = $"{journey.Title} (E2E Journey Trace)",
            GivenConditions = givenEntries,
            WhenSteps = whenSteps,
            ThenTextJa = thenJa,
            ThenTextEn = thenEn,
            ExpectedOutcome = outcome,
            ReviewNoteJa = reviewNoteJa,
            ReviewNoteEn = reviewNoteEn,
            Tags = tags
        };
    }

    private static EvaluationOutcome EvaluateAllRequirementsInternal(
        IReadOnlyDictionary<string, string> stateDict,
        string? triggerEventRef,
        GenerationContext ctx)
    {
        var matchedProhibits = new List<CanonicalRequirementNode>();
        var matchedNormals = new List<CanonicalRequirementNode>();

        foreach (var req in ctx.AtomicRequirements)
        {
            if (req.Behavior is null || !ctx.TryGetBehaviorDef(req.Behavior.BehaviorDefRef, out var behDef))
            {
                continue;
            }

            if (!DoesTriggerMatch(req, behDef, triggerEventRef, ctx))
            {
                continue;
            }

            if (!DoStatesMatchStrict(req, behDef, stateDict))
            {
                continue;
            }

            if (behDef.Effect == SemanticEffect.Prohibit)
            {
                matchedProhibits.Add(req);
            }
            else
            {
                matchedNormals.Add(req);
            }
        }

        // セクション 7.1: Prohibit が 1 件でも含まれていれば Blocked を優先
        if (matchedProhibits.Count > 0)
        {
            var ids = matchedProhibits.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
            var idQuoted = string.Join(", ", ids.Select(id => $"\"{id}\""));
            return new EvaluationOutcome
            {
                ExpectedOutcome = ExpectedTestOutcome.Blocked,
                MatchedRequirementIds = ids,
                ThenTextJa = $"{idQuoted} により操作がブロックされ、状態が維持されること",
                ThenTextEn = $"Operation is blocked by {idQuoted} and state remains unchanged"
            };
        }

        if (matchedNormals.Count > 0)
        {
            var orderedNormals = matchedNormals.OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
            var ids = orderedNormals.Select(r => r.Id).ToList();

            var partsJa = new List<string>(orderedNormals.Count);
            var partsEn = new List<string>(orderedNormals.Count);
            foreach (var r in orderedNormals)
            {
                var def = ctx.BehaviorById[r.Behavior!.BehaviorDefRef];
                var (_, descJa, descEn) = BuildPositiveExpectedResult(r, def, ctx);
                partsJa.Add($"代替要求 \"{r.Id}\" が発火し、{descJa}");
                partsEn.Add($"Alternative requirement \"{r.Id}\" fires: {descEn}");
            }

            return new EvaluationOutcome
            {
                ExpectedOutcome = ExpectedTestOutcome.AlternativeFired,
                MatchedRequirementIds = ids,
                ThenTextJa = string.Join(" / ", partsJa),
                ThenTextEn = string.Join(" / ", partsEn)
            };
        }

        return new EvaluationOutcome
        {
            ExpectedOutcome = ExpectedTestOutcome.NoOp,
            MatchedRequirementIds = [],
            ThenTextJa = "該当する要求が存在しないため、対象状態は変化しないこと",
            ThenTextEn = "No matching requirement exists; target state remains unchanged"
        };
    }

    private static bool DoesTriggerMatch(
        CanonicalRequirementNode req,
        BehaviorDefinition behDef,
        string? triggerEventRef,
        GenerationContext ctx)
    {
        if (!string.IsNullOrWhiteSpace(req.TriggerEventRef))
        {
            return string.Equals(req.TriggerEventRef, triggerEventRef, StringComparison.Ordinal);
        }

        if (behDef.Effect == SemanticEffect.Prohibit)
        {
            if (string.IsNullOrWhiteSpace(triggerEventRef))
            {
                return true;
            }

            return ctx.GetEffectiveTriggersForProhibit(req).Contains(triggerEventRef);
        }

        // トリガー未指定の通常要求はトリガー未指定評価時のみ一致
        return string.IsNullOrWhiteSpace(triggerEventRef);
    }

    /// <summary>
    /// セクション 7.1 準拠: 要求のすべての前提条件および FromValue の変数が
    /// stateDict に存在し、かつ値を満たす場合のみ true。
    /// </summary>
    private static bool DoStatesMatchStrict(
        CanonicalRequirementNode req,
        BehaviorDefinition behDef,
        IReadOnlyDictionary<string, string> stateDict)
    {
        foreach (var clause in req.Preconditions)
        {
            if (string.IsNullOrWhiteSpace(clause.StateTermRef)
                || !stateDict.TryGetValue(clause.StateTermRef, out var actual)
                || string.IsNullOrWhiteSpace(actual)
                || !IsPreconditionSatisfied(clause, actual))
            {
                return false;
            }
        }

        var behavior = req.Behavior!;
        if ((behDef.Effect == SemanticEffect.MutateState || behDef.Effect == SemanticEffect.Prohibit)
            && !string.IsNullOrWhiteSpace(behavior.FromValue))
        {
            if (string.IsNullOrWhiteSpace(behavior.TargetTermRef)
                || !stateDict.TryGetValue(behavior.TargetTermRef, out var actualTarget)
                || !string.Equals(actualTarget, behavior.FromValue, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesMatrixCell(
        CanonicalRequirementNode req,
        string lcTermId,
        string fromState,
        string eventTermId,
        GenerationContext ctx)
    {
        if (req.Behavior is null || !ctx.TryGetBehaviorDef(req.Behavior.BehaviorDefRef, out var behDef))
        {
            return false;
        }

        if (!DoesTriggerMatch(req, behDef, eventTermId, ctx))
        {
            return false;
        }

        if (string.Equals(req.Behavior.TargetTermRef, lcTermId, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(req.Behavior.FromValue)
            && !string.Equals(req.Behavior.FromValue, fromState, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var clause in req.Preconditions)
        {
            if (string.Equals(clause.StateTermRef, lcTermId, StringComparison.Ordinal)
                && !IsPreconditionSatisfied(clause, fromState))
            {
                return false;
            }
        }

        return true;
    }

    private static (ExpectedTestOutcome Outcome, string ThenJa, string ThenEn) BuildPositiveExpectedResult(
        CanonicalRequirementNode req,
        BehaviorDefinition behDef,
        GenerationContext ctx)
    {
        var behavior = req.Behavior!;
        var targetJa = ctx.ResolveDisplayName(behavior.TargetTermRef);
        var targetEn = ctx.ResolveCanonicalName(behavior.TargetTermRef);

        if (behDef.Effect == SemanticEffect.MutateState)
        {
            var toRaw = behavior.ToValue ?? string.Empty;
            var toJa = ctx.ResolveValueLabelJa(behavior.TargetTermRef, toRaw);
            return (
                ExpectedTestOutcome.StateChanged,
                $"\"{targetJa}\" が \"{toJa}\" になること",
                $"\"{targetEn}\" becomes \"{toRaw}\"");
        }

        var actionJa = !string.IsNullOrWhiteSpace(behDef.JapaneseLabel) ? behDef.JapaneseLabel : behDef.CanonicalLabel;
        var actionEn = !string.IsNullOrWhiteSpace(behDef.CanonicalLabel) ? behDef.CanonicalLabel : behDef.BehaviorDefId;
        return (
            ExpectedTestOutcome.ActionEmitted,
            $"\"{targetJa}\" に対して \"{actionJa}\" が実行されること",
            $"\"{actionEn}\" is executed on \"{targetEn}\"");
    }

    private static List<TestConditionEntry> BuildConditionEntries(
        IReadOnlyDictionary<string, string> stateDict,
        GenerationContext ctx)
    {
        // Glossary 定義順で並べ、未登録 ID は末尾に Ordinal 順
        var orderedKeys = stateDict.Keys
            .OrderBy(k => ctx.GetGlossaryOrder(k))
            .ThenBy(k => k, StringComparer.Ordinal)
            .ToList();

        var entries = new List<TestConditionEntry>(orderedKeys.Count);
        foreach (var termId in orderedKeys)
        {
            var rawValue = stateDict[termId];
            entries.Add(new TestConditionEntry
            {
                TermId = termId,
                LabelJa = ctx.ResolveDisplayName(termId),
                LabelEn = ctx.ResolveCanonicalName(termId),
                RawValue = rawValue,
                ValueLabelJa = ctx.ResolveValueLabelJa(termId, rawValue),
                ValueLabelEn = rawValue
            });
        }

        return entries;
    }

    private static List<TestActionEntry> BuildSingleWhenStep(
        string? actorRef,
        string? eventRef,
        string? reqRef,
        GenerationContext ctx)
    {
        var actorJa = !string.IsNullOrWhiteSpace(actorRef) ? ctx.ResolveDisplayName(actorRef) : null;
        var actorEn = !string.IsNullOrWhiteSpace(actorRef) ? ctx.ResolveCanonicalName(actorRef) : null;
        var eventJa = !string.IsNullOrWhiteSpace(eventRef) ? ctx.ResolveDisplayName(eventRef) : null;
        var eventEn = !string.IsNullOrWhiteSpace(eventRef) ? ctx.ResolveCanonicalName(eventRef) : null;

        string actionJa;
        string actionEn;
        if (!string.IsNullOrWhiteSpace(eventJa) && !string.IsNullOrWhiteSpace(actorJa))
        {
            actionJa = $"\"{actorJa}\" が \"{eventJa}\" を実行する";
            actionEn = $"\"{actorEn}\" triggers \"{eventEn}\"";
        }
        else if (!string.IsNullOrWhiteSpace(eventJa))
        {
            actionJa = $"\"{eventJa}\" が発生する";
            actionEn = $"\"{eventEn}\" occurs";
        }
        else
        {
            actionJa = "システム状態を評価する";
            actionEn = "System state is evaluated";
        }

        return
        [
            new TestActionEntry
            {
                StepNumber = 1,
                ActorTermId = actorRef,
                ActorLabelJa = actorJa,
                ActorLabelEn = actorEn,
                EventTermId = eventRef,
                EventLabelJa = eventJa,
                EventLabelEn = eventEn,
                ActionTextJa = actionJa,
                ActionTextEn = actionEn,
                RequirementRef = reqRef
            }
        ];
    }

    private static string? GetSatisfyingValue(PreconditionClause clause, GenerationContext ctx)
    {
        if (clause.Operator is ComparisonOperator.EQ or ComparisonOperator.IN)
        {
            return clause.ExpectedValues.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
        }

        if (clause.Operator == ComparisonOperator.NEQ)
        {
            var domainValues = GetDomainValues(clause.StateTermRef, ctx);
            return domainValues.FirstOrDefault(v => !clause.ExpectedValues.Contains(v, StringComparer.Ordinal));
        }

        return clause.ExpectedValues.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    private static List<string> GetNegatedValues(PreconditionClause clause, GenerationContext ctx)
    {
        var domainValues = GetDomainValues(clause.StateTermRef, ctx);
        if (clause.Operator is ComparisonOperator.EQ or ComparisonOperator.IN)
        {
            return domainValues
                .Where(v => !clause.ExpectedValues.Contains(v, StringComparer.Ordinal))
                .ToList();
        }

        if (clause.Operator == ComparisonOperator.NEQ)
        {
            var inDomain = domainValues
                .Where(v => clause.ExpectedValues.Contains(v, StringComparer.Ordinal))
                .ToList();
            return inDomain.Count > 0
                ? inDomain
                : clause.ExpectedValues.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).ToList();
        }

        return [];
    }

    private static List<string> GetNegatedValuesForExactValue(string termId, string exactValue, GenerationContext ctx)
    {
        var domainValues = GetDomainValues(termId, ctx);
        return domainValues
            .Where(v => !string.Equals(v, exactValue, StringComparison.Ordinal))
            .ToList();
    }

    private static List<string> GetDomainValues(string termId, GenerationContext ctx)
    {
        if (!ctx.GlossaryById.TryGetValue(termId, out var term))
        {
            return [];
        }

        return term.LogicalType.Kind switch
        {
            LogicalTypeKind.Boolean => ["true", "false"],
            LogicalTypeKind.Enum => term.LogicalType.AllowedValues
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            _ => []
        };
    }

    private static bool IsPreconditionSatisfied(PreconditionClause clause, string actualValue) => clause.Operator switch
    {
        ComparisonOperator.EQ or ComparisonOperator.IN =>
            clause.ExpectedValues.Contains(actualValue, StringComparer.Ordinal),
        ComparisonOperator.NEQ =>
            !clause.ExpectedValues.Contains(actualValue, StringComparer.Ordinal),
        _ => false
    };

    private static List<string> BuildUnitTags(
        string? rationaleId,
        string? compositeId,
        string reqId,
        TestTechnique technique,
        ExpectedTestOutcome outcome)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(rationaleId)) tags.Add($"@{rationaleId}");
        if (!string.IsNullOrWhiteSpace(compositeId)) tags.Add($"@{compositeId}");
        tags.Add($"@{reqId}");
        tags.Add("@level:unit");
        tags.Add($"@technique:{FormatTechniqueTag(technique)}");
        if (technique == TestTechnique.SafetyInterlock || outcome == ExpectedTestOutcome.Blocked)
        {
            tags.Add("@safety");
        }
        return tags;
    }

    private static List<string> BuildFsmTags(
        string lcTermId,
        TestTechnique technique,
        ExpectedTestOutcome outcome,
        List<string> relatedReqIds)
    {
        var tags = new List<string>
        {
            $"@{lcTermId}",
            "@level:component",
            $"@technique:{FormatTechniqueTag(technique)}"
        };
        foreach (var reqId in relatedReqIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            tags.Add($"@{reqId}");
        }
        if (technique == TestTechnique.ExplicitProhibit || outcome == ExpectedTestOutcome.Blocked)
        {
            tags.Add("@safety");
        }
        return tags;
    }

    internal static string FormatTechniqueTag(TestTechnique technique) => technique switch
    {
        TestTechnique.ConditionNormal => "condition-normal",
        TestTechnique.ConditionNegation => "condition-negation",
        TestTechnique.SafetyInterlock => "safety-interlock",
        TestTechnique.ValidTransition => "valid-transition",
        TestTechnique.ConditionalTransition => "conditional-transition",
        TestTechnique.ExplicitProhibit => "explicit-prohibit",
        TestTechnique.InvalidTransition => "invalid-transition",
        TestTechnique.JourneyTrace => "journey-trace",
        _ => technique.ToString().ToLowerInvariant()
    };

    private static string ShortenTermId(string termId)
    {
        var trimmed = termId.StartsWith("TERM-", StringComparison.Ordinal)
            ? termId[5..]
            : termId;
        return trimmed.Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (k, v) in left)
        {
            if (!right.TryGetValue(k, out var rv) || !string.Equals(v, rv, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 同一 SourceRef 内での並び順優先度:
    /// POS (ConditionNormal) -> NEG (ConditionNegation) -> BLK -> UNB -> その他
    /// </summary>
    internal static int GetCaseSortPriority(DerivedTestCase tc) => tc.Technique switch
    {
        TestTechnique.ConditionNormal => 0,
        TestTechnique.ConditionNegation => 1,
        TestTechnique.SafetyInterlock when tc.TestCaseId.Contains("-BLK-", StringComparison.Ordinal) => 2,
        TestTechnique.SafetyInterlock when tc.TestCaseId.Contains("-UNB-", StringComparison.Ordinal) => 3,
        TestTechnique.SafetyInterlock => 4,
        _ => 5
    };

    private static string FormatJourneyStepSummary(
        JourneyStepResult step,
        GenerationContext ctx,
        bool isJa)
    {
        if (step.Status is not (JourneyStepStatus.Passed or JourneyStepStatus.Blocked)
            || !ctx.RequirementById.TryGetValue(step.RequirementRef, out var req)
            || req.Behavior is null
            || !ctx.TryGetBehaviorDef(req.Behavior.BehaviorDefRef, out var behDef))
        {
            return !string.IsNullOrWhiteSpace(step.TransitionSummary)
                ? step.TransitionSummary
                : step.Message;
        }

        var behavior = req.Behavior;
        var targetName = isJa
            ? ctx.ResolveDisplayName(behavior.TargetTermRef)
            : ctx.ResolveCanonicalName(behavior.TargetTermRef);

        if (behDef.Effect == SemanticEffect.MutateState)
        {
            var toVal = behavior.ToValue ?? string.Empty;
            var toLabel = isJa ? ctx.ResolveValueLabelJa(behavior.TargetTermRef, toVal) : toVal;

            if (!string.IsNullOrWhiteSpace(behavior.FromValue))
            {
                var fromLabel = isJa
                    ? ctx.ResolveValueLabelJa(behavior.TargetTermRef, behavior.FromValue)
                    : behavior.FromValue;
                return isJa
                    ? $"{targetName}: {fromLabel} → {toLabel}"
                    : $"{targetName}: {fromLabel} -> {toLabel}";
            }

            return $"{targetName}: {toLabel}";
        }

        if (behDef.Effect == SemanticEffect.Prohibit)
        {
            var prohibitedAction = isJa ? "操作" : "OPERATION";
            if (!string.IsNullOrWhiteSpace(behavior.ProhibitedBehaviorDefRef))
            {
                if (ctx.TryGetBehaviorDef(behavior.ProhibitedBehaviorDefRef, out var pDef))
                {
                    prohibitedAction = isJa
                        ? (!string.IsNullOrWhiteSpace(pDef.JapaneseLabel) ? pDef.JapaneseLabel : pDef.CanonicalLabel)
                        : (!string.IsNullOrWhiteSpace(pDef.CanonicalLabel) ? pDef.CanonicalLabel : pDef.BehaviorDefId);
                }
                else
                {
                    prohibitedAction = behavior.ProhibitedBehaviorDefRef;
                }
            }

            if (isJa)
            {
                var toSuffix = !string.IsNullOrWhiteSpace(behavior.ProhibitedToValue)
                    ? $"（遷移先: {ctx.ResolveValueLabelJa(behavior.TargetTermRef, behavior.ProhibitedToValue)}）"
                    : string.Empty;
                return $"{targetName}の{prohibitedAction}を禁止{toSuffix}";
            }
            else
            {
                var toSuffix = !string.IsNullOrWhiteSpace(behavior.ProhibitedToValue)
                    ? $" (to {behavior.ProhibitedToValue})"
                    : string.Empty;
                return $"PROHIBIT {prohibitedAction} on {targetName}{toSuffix}";
            }
        }

        var actionLabel = isJa
            ? (!string.IsNullOrWhiteSpace(behDef.JapaneseLabel) ? behDef.JapaneseLabel : behDef.CanonicalLabel)
            : (!string.IsNullOrWhiteSpace(behDef.CanonicalLabel) ? behDef.CanonicalLabel : behDef.BehaviorDefId);
        return $"{targetName}: {actionLabel}";
    }

    /// <summary>
    /// セクション 3.2 固定ソート順:
    /// TestLevel (Unit -> Component -> E2E) -> RationaleId -> CompositeId -> SourceRef -> 技法優先順 (POS -> NEG -> BLK -> UNB) -> TestCaseId
    /// </summary>
    private static List<DerivedTestCase> SortTestCases(IEnumerable<DerivedTestCase> cases) =>
        cases
            .OrderBy(c => c.TestLevel)
            .ThenBy(c => string.IsNullOrEmpty(c.RationaleId) ? 1 : 0)
            .ThenBy(c => c.RationaleId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => string.IsNullOrEmpty(c.CompositeId) ? 1 : 0)
            .ThenBy(c => c.CompositeId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(c => c.SourceRef, StringComparer.Ordinal)
            .ThenBy(GetCaseSortPriority)
            .ThenBy(c => c.TestCaseId, StringComparer.Ordinal)
            .ToList();

    private sealed class GenerationContext
    {
        private readonly Dictionary<string, int> _glossaryOrder = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _prohibitTriggerCache = new(StringComparer.Ordinal);

        public KnowledgeGraphDocument Document { get; }
        public Dictionary<string, ApprovedGlossaryTerm> GlossaryById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, BehaviorDefinition> BehaviorById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, RationaleNode> RationaleById { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, CanonicalRequirementNode> RequirementById { get; } = new(StringComparer.Ordinal);
        public List<CanonicalRequirementNode> AtomicRequirements { get; }

        public GenerationContext(KnowledgeGraphDocument document)
        {
            Document = document;

            for (var i = 0; i < document.Glossary.Count; i++)
            {
                var term = document.Glossary[i];
                if (!string.IsNullOrWhiteSpace(term.Id))
                {
                    GlossaryById.TryAdd(term.Id, term);
                    _glossaryOrder.TryAdd(term.Id, i);
                }
            }

            foreach (var def in document.BehaviorCatalog)
            {
                if (!string.IsNullOrWhiteSpace(def.BehaviorDefId))
                {
                    BehaviorById.TryAdd(def.BehaviorDefId, def);
                }
            }

            foreach (var rat in document.Rationales)
            {
                if (!string.IsNullOrWhiteSpace(rat.Id))
                {
                    RationaleById.TryAdd(rat.Id, rat);
                }
            }

            foreach (var req in document.Requirements)
            {
                if (!string.IsNullOrWhiteSpace(req.Id))
                {
                    RequirementById.TryAdd(req.Id, req);
                }
            }

            AtomicRequirements = document.Requirements
                .Where(r => r.NodeKind == RequirementNodeKind.Atomic)
                .OrderBy(r => r.Id, StringComparer.Ordinal)
                .ToList();
        }

        public bool TryGetBehaviorDef(string? behaviorDefRef, out BehaviorDefinition def)
        {
            if (!string.IsNullOrWhiteSpace(behaviorDefRef) && BehaviorById.TryGetValue(behaviorDefRef, out var found))
            {
                def = found;
                return true;
            }

            def = null!;
            return false;
        }

        public bool IsProhibit(CanonicalRequirementNode req) =>
            req.Behavior is not null
            && TryGetBehaviorDef(req.Behavior.BehaviorDefRef, out var def)
            && def.Effect == SemanticEffect.Prohibit;

        public int GetGlossaryOrder(string termId) =>
            _glossaryOrder.TryGetValue(termId, out var idx) ? idx : int.MaxValue;

        public string ResolveDisplayName(string? termRef)
        {
            if (!string.IsNullOrWhiteSpace(termRef) && GlossaryById.TryGetValue(termRef, out var term))
            {
                return term.GetDisplayName();
            }

            return termRef ?? string.Empty;
        }

        public string ResolveCanonicalName(string? termRef)
        {
            if (!string.IsNullOrWhiteSpace(termRef) && GlossaryById.TryGetValue(termRef, out var term))
            {
                return term.CanonicalName;
            }

            return termRef ?? string.Empty;
        }

        public string ResolveValueLabelJa(string? termRef, string value)
        {
            if (!string.IsNullOrWhiteSpace(termRef) && GlossaryById.TryGetValue(termRef, out var term))
            {
                return term.LogicalType.GetValueLabel(value);
            }

            return value;
        }

        public string? ResolveRationaleId(CanonicalRequirementNode req)
        {
            var direct = req.RationaleRefs.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
            if (direct is not null)
            {
                return direct;
            }

            if (!string.IsNullOrWhiteSpace(req.ParentRequirementId)
                && RequirementById.TryGetValue(req.ParentRequirementId, out var parent))
            {
                return parent.RationaleRefs.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));
            }

            return null;
        }

        public (string TitleJa, string TitleEn) ResolveViewpointTitle(CanonicalRequirementNode req)
        {
            if (!string.IsNullOrWhiteSpace(req.ParentRequirementId)
                && RequirementById.TryGetValue(req.ParentRequirementId, out var parent))
            {
                return (parent.Title, parent.Title);
            }

            return (req.Title, req.Title);
        }

        public List<CanonicalRequirementNode> FindMatchingNormalRequirementsForProhibit(CanonicalRequirementNode prohibitReq)
        {
            var beh = prohibitReq.Behavior;
            if (beh is null)
            {
                return [];
            }

            return AtomicRequirements
                .Where(n => n.Behavior is not null
                            && !IsProhibit(n)
                            && string.Equals(n.Behavior.TargetTermRef, beh.TargetTermRef, StringComparison.Ordinal)
                            && (string.IsNullOrWhiteSpace(beh.ProhibitedBehaviorDefRef)
                                || string.Equals(n.Behavior.BehaviorDefRef, beh.ProhibitedBehaviorDefRef, StringComparison.Ordinal))
                            && (string.IsNullOrWhiteSpace(beh.ProhibitedToValue)
                                || string.Equals(n.Behavior.ToValue, beh.ProhibitedToValue, StringComparison.Ordinal)))
                .ToList();
        }

        public HashSet<string> GetEffectiveTriggersForProhibit(CanonicalRequirementNode prohibitReq)
        {
            if (_prohibitTriggerCache.TryGetValue(prohibitReq.Id, out var cached))
            {
                return cached;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(prohibitReq.TriggerEventRef))
            {
                set.Add(prohibitReq.TriggerEventRef);
            }
            else
            {
                foreach (var normal in FindMatchingNormalRequirementsForProhibit(prohibitReq))
                {
                    if (!string.IsNullOrWhiteSpace(normal.TriggerEventRef))
                    {
                        set.Add(normal.TriggerEventRef);
                    }
                }
            }

            _prohibitTriggerCache[prohibitReq.Id] = set;
            return set;
        }
    }
}
