using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StepGameManager : GameManager
{
    protected override void ProcessGameEnd()
    {
        
    }

    protected override void ProcessGameStart()
    {
        Physics2D.simulationMode = SimulationMode2D.Script;
    }
}
