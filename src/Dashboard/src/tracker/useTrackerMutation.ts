import { useMutation, useQueryClient } from '@tanstack/react-query';

// A tracker write. On success it runs onDone (reset a form, navigate away) and then refreshes every tracker query.
// On failure it runs onFail, if given.
export function useTrackerMutation<TInput, TResult = unknown>(
  write: (input: TInput) => Promise<TResult>,
  onDone?: (result: TResult, input: TInput) => void,
  onFail?: (error: Error, input: TInput) => void,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: write,
    onError: onFail,
    onSuccess: async (result, input) => {
      onDone?.(result, input);
      await queryClient.invalidateQueries({ queryKey: ['tracker'] });
    },
  });
}
