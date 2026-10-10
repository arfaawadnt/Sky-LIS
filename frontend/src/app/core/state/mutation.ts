import { signal } from '@angular/core';

import { ProblemCodes, ProblemError, type ProblemDetails } from '../api/problem-details';

export interface Mutation<TIn, TOut> {
  readonly pending: () => boolean;
  readonly problem: () => ProblemDetails | null;
  execute(input: TIn): Promise<TOut | null>;
  reset(): void;
}

function toProblem(error: unknown): ProblemDetails {
  if (error instanceof ProblemError) {
    return error.problem;
  }

  return {
    type: 'about:blank',
    title: error instanceof Error ? error.message : 'The request failed.',
    status: 0,
    code: ProblemCodes.unexpected,
    traceId: null,
    correlationId: null,
    errors: null,
  };
}

/**
 * The shared write helper of 01 §6.3: one `pending` and one `problem` signal per mutation, so no facade repeats
 * the boilerplate (pitfall P-63). The result is handed to `then` for cache reloads.
 */
export function mutation<TIn, TOut>(run: (input: TIn) => Promise<TOut>, options?: { then?: (out: TOut) => void }): Mutation<TIn, TOut> {
  const pending = signal(false);
  const problem = signal<ProblemDetails | null>(null);

  return {
    pending: pending.asReadonly(),
    problem: problem.asReadonly(),
    async execute(input: TIn): Promise<TOut | null> {
      pending.set(true);
      problem.set(null);
      try {
        const out = await run(input);
        options?.then?.(out);
        return out;
      } catch (error: unknown) {
        problem.set(toProblem(error));
        return null;
      } finally {
        pending.set(false);
      }
    },
    reset(): void {
      pending.set(false);
      problem.set(null);
    },
  };
}
