namespace Infrastructure.Workers;

/// <summary>Named diagnostic messages and structured log templates for BlockchainIndexerWorker.</summary>
internal static class BlockchainIndexerWorkerDiagnostics
{
    public const string WebSocketFallback = "Blockchain websocket stream dropped. Falling back to RPC polling.";
    public const string PublishedBlockToTheProcessingPipelineAt = "Published block {BlockNumber} to the processing pipeline at {Timestamp}.";
    public const string BlockCouldNotBeLoadedAndWasSkipped = "Block {BlockNumber} could not be loaded and was skipped.";
}
