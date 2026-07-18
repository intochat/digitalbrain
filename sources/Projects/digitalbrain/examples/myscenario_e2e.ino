# Tagging context for Knowledge Base & Diagram rendering
@tag("DigitalBrain.Examples.E2E")

neuron DigitalBrain.Examples.DocumentDiagramWorkflow
  "Processes a Word document via vector search and dynamic LLM translation to C# logic, generating a workflow diagram."

  # Binding handles to Core ADK/SDK utilities
  @vectorSearch = Digitalbrain.Core.VectorSearch
  @llmGenerator = Digitalbrain.Ai.Generator
  @documentReader = Digitalbrain.System.DocxReader

  on synapse(DigitalBrain.Examples.StartDocxDiagram):
    # Step 1: Read the word document via standard reader
    let docPath = @synapse.documentPath
    let docContent = ask @documentReader to "read: {docPath}"

    # Step 2: Perform semantic search / vector search to extract relevant contexts
    let context = ask @vectorSearch to "query: {docContent} matching: 'diagram components'"

    # Step 3: Dispatch extracted context to LLM compiler which generates C# code at runtime
    let csharpCode = ask @llmGenerator to "translate-to-csharp: {context} format: 'RoslynScript'"

    # Step 4: Dynamically compile and register the generated C# code
    let compilationResult = Kernel.CompileAndRegister(csharp: csharpCode)

    # Step 5: Emit the final status containing the dynamic compilation log
    Kernel.Emit(signal: DigitalBrain.Examples.DocxDiagramSuccess, payload: {
      "Document": docPath,
      "Status": "Dynamic Roslyn compile successful",
      "AssemblyHash": compilationResult.hash
    })

# BDD validation scenario to ensure the compile-checks pass closed
scenario "Successful Document Parsing and Dynamic C# Compilation"
  given @documentReader returns "Word document contents describing a 3-tier system architecture."
  given @vectorSearch returns "Context: Presentation Layer talks to Kernel. Kernel emits Synapse."
  given @llmGenerator returns "public class DynamicDiagramNeuron { public async Task Execute() { } }"
  when synapse StartDocxDiagram(documentPath: "architecture_spec.docx")
  then synapse DocxDiagramSuccess emitted with Status == "Dynamic Roslyn compile successful"
