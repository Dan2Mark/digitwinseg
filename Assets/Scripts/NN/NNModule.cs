using Unity.Barracuda;
using UnityEngine;

public abstract class NNModule : MonoBehaviour
{
    [Header("Model")]
    public NNModel modelAsset;
    protected Model runtimeModel;
    protected IWorker worker;

    protected virtual void Awake()
    {
        runtimeModel = ModelLoader.Load(modelAsset);
        worker = WorkerFactory.CreateWorker(WorkerFactory.Type.ComputePrecompiled, runtimeModel);
    }

    public virtual Tensor Run(Tensor input)
    {
        Debugger.Log($"Running model: {modelAsset.name}, input shape: {input.shape}");
        worker.Execute(input);
        Debugger.Log($"Model executed: {modelAsset.name}");
        return worker.PeekOutput();
    }

    public abstract Tensor PrepareInput(Texture2D source);
    public abstract void ProcessOutput(Tensor output);

    protected virtual void OnDestroy()
    {
        worker?.Dispose();
    }
}
