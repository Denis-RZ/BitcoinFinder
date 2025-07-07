using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

namespace BitcoinFinder
{
    public interface ISeedPhraseGpuSearcher
    {
        Task<SearchResult> SearchAsync(SearchParameters parameters, CancellationToken token);
    }

    public class SearchResult
    {
        public int CheckedCount { get; set; }
        public string FoundPhrase { get; set; }
    }

    public class GpuSeedPhrasePlugin : ISeedPhraseGpuSearcher
    {
        public async Task<SearchResult> SearchAsync(SearchParameters parameters, CancellationToken token)
        {
            // Пример: перебор массива строк и подсчёт их длины на GPU/CPU
            string[] phrases = Enumerable.Range(0, 1000)
                .Select(i => $"test seed phrase {i}").ToArray();
            int[] lengths = new int[phrases.Length];

            using var context = Context.CreateDefault();
            using var accelerator = context.GetPreferredDevice(preferCPU: true).CreateAccelerator(context);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                (index, input, output) => { output[index] = input[index]; });

            // Для примера: просто длины строк
            int[] input = phrases.Select(p => p.Length).ToArray();
            using var inputBuffer = accelerator.Allocate1D(input);
            using var outputBuffer = accelerator.Allocate1D<int>(input.Length);

            kernel((int)inputBuffer.Length, inputBuffer.View, outputBuffer.View);
            accelerator.Synchronize();

            int[] result = outputBuffer.GetAsArray1D();
            // Для демонстрации: ищем первую строку длиной > 20
            int foundIdx = Array.FindIndex(result, l => l > 20);
            string found = foundIdx >= 0 ? phrases[foundIdx] : null;

            return new SearchResult
            {
                CheckedCount = result.Length,
                FoundPhrase = found
            };
        }
    }
} 