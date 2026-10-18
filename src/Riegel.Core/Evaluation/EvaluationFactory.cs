// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Evaluation;

/// <summary>
/// Factory der Auswertung (Paket engine; Signatur fest). Die Engine bewertet jede Regel des Katalogs nach spec 2.3/2.4
/// zu einem rohen Status mit Flags; die Herkunft der Werte kommt aus <see cref="RiegelServices.Attribution"/>.
/// </summary>
public static class EvaluationFactory
{
    public static IEvaluator Create(RiegelServices services) => new Evaluator(services);
}
