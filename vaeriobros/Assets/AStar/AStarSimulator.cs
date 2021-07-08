using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Simplified version of https://github.com/jumoel/mario-astar-robinbaumgarten

public class AStarSimulator
{
    public float MARIOSPEED;
    public float MARIOHEIGHT = 1.9f;

    public float tickTime = 0.02f; // defined in the unity time settings

    // LevelScene objects store all the information about the environment,
    // Mario and enemies. 
    private LevelScene levelScene;  		// current world state
    public LevelScene workScene;   		// world state used by the planner (some ticks in the future)
    public SearchNode bestPosition; 	// the current best position found by the planner
    public SearchNode furthestPosition; // the furthest position found by the planner (sometimes different than best)
    float currentSearchStartingMarioXPos;
    List<SearchNode> posPool;		// the open-list of A*, contains all the unexplored search nodes
    List<float[]> visitedStates = new List<float[]>(); // the closed-list of A*

    public int timeBudget = 20; // ms
    public static int visitedListPenalty = 1500; // penalty for being in the visited-states list

    private List<bool[]> currentActionPlan; // the plan generated by the panner
    int ticksBeforeReplanning = 0;


    static int DAMAGEPENALTY = 1000000;
    static int DAMAGEPENALTYTIMEFACTOR = 100;
    static int maxRight = 4;                 // distance to plan to the right

    // Values to measure when a node was already visited
    // these values can be tweaked
    // TODO dirty numbers
    float timeDiff = 0.1f;
    float xDiff = 0.1f;
    float yDiff = 0.1f;

    // How many actions Mario makes for each search step (can be tweaked)
    public int stepsPerSearch = 6;

    public static float maxMarioSpeed = SharedData.runSpeed;

    /// <summary>
    /// A SearchNode is a node in the A* search, consisting of an action, the world state using this action
	/// and information about the parent.
    /// </summary>
	public class SearchNode
    {
        public int timeElapsed = 0;            // How much ticks elapsed since start of search
        public float remainingTimeEstimated = 0; // Optimal (estimated) time to reach goal
        public float remainingTime = 0;        // Optimal time to reach goal AFTER simulating with the selected action

        public SearchNode parentPos = null;     // Parent node
        public LevelScene sceneSnapshot = null; // World state of this node
        public bool hasBeenHurt = false;
        public bool isInVisitedList = false;

        public bool[] action;                   // the action of this node
        public int repetitions;    // how often this action is repeat for this node

        private AStarSimulator outer; // we need a referecne to the the outer class to simulate java behaviour 

        public SearchNode(bool[] action, int repetitions, SearchNode parent, AStarSimulator _outer)
        {
            this.parentPos = parent;
            this.action = action;
            this.repetitions = repetitions;
            this.outer = _outer;

            if (parent != null)
            {
                this.remainingTimeEstimated = parent.estimateRemainingTimeChild(action, repetitions);
                timeElapsed = parent.timeElapsed + repetitions;
            }
            else
            {
                this.remainingTimeEstimated = calcRemainingTime(outer.levelScene.plumberXposition, 0);
                timeElapsed = 0;
            }
        }

        // returns the estimated remaining time to some arbitrary distant target
        // TODO is XA = X acceleration?
        // TODO dirty numbers
        public float calcRemainingTime(float marioX, float marioXA)
        {

            return (100000 - (outer.maxForwardMovement(marioXA, 1000) + marioX))
                / maxMarioSpeed - 1000;
        }

        public float getRemainingTime()
        {
            if (remainingTime > 0)
                return remainingTime;
            else
                return remainingTimeEstimated;
        }

        // estimate the time remaining to the goal for a child (that uses action)
        public float estimateRemainingTimeChild(bool[] action, int repetitions)
        {
            float[] childbehaviorDistanceAndSpeed = outer.estimateMaximumForwardMovement(
                    outer.levelScene.plumberXposition, action, repetitions);
            return calcRemainingTime(outer.levelScene.plumberXposition + childbehaviorDistanceAndSpeed[0],
                    childbehaviorDistanceAndSpeed[1]);
        }

        // Simulate the world state after we applied the action of this node, using the parent world state
        public float simulatePos()
        {
            // set state to parents scene
            outer.levelScene = parentPos.sceneSnapshot;
            parentPos.sceneSnapshot = outer.backupState(); // copy the parents scene since we will change this scene

            int initialDamage = outer.getMarioDamage();
            for (int i = 0; i < repetitions; i++)
            {

                // Run the simulator
                outer.advanceStep(action);
                // TODO this ticks the levelScene so we have to find a way to tick a given level scene too.

            }

            // set the remaining time after we've simulated the effects of our action,
            // penalising it if we've been hurt.
            remainingTime = calcRemainingTime(outer.levelScene.plumberXposition, outer.levelScene.plumberXacceleration)
                 + (outer.getMarioDamage() - initialDamage) * (DAMAGEPENALTY - DAMAGEPENALTYTIMEFACTOR * timeElapsed);
            if (isInVisitedList)
                remainingTime += visitedListPenalty;
            hasBeenHurt = (outer.getMarioDamage() - initialDamage) != 0;
            sceneSnapshot = outer.backupState();

            return remainingTime;
        }

        public List<SearchNode> generateChildren()
        {
            List<SearchNode> list = new List<SearchNode>();
            List<bool[]> possibleActions = createPossibleActions(this);

            foreach (bool[] action in possibleActions)
            {
                list.Add(new SearchNode(action, repetitions, this, this.outer)); // hope this.outer works as I expect to replace the Java functionality
            }
            return list;
        }

        // Create a list of (almost) all valid actions possible in our node
        public List<bool[]> createPossibleActions(SearchNode currentPos)
        {
            List<bool[]> possibleActions = new List<bool[]>();

            // jump
            if (outer.canJumpHigher(currentPos, true)) possibleActions.Add(createAction(false, false, true));
            // possibleActions.Add(createAction(false, false, true));

            // run right
            possibleActions.Add(createAction(false, true, false));
            if (outer.canJumpHigher(currentPos, true)) possibleActions.Add(createAction(false, true, true));
            // possibleActions.Add(createAction(false, true, true));

            // run left
            possibleActions.Add(createAction(true, false, false));
            if (outer.canJumpHigher(currentPos, true)) possibleActions.Add(createAction(true, false, true));
            // possibleActions.Add(createAction(true, false, true));

            return possibleActions;
        }
    }

    public AStarSimulator()
    {
        levelScene = new LevelScene();
        levelScene.GetCurrentScene();
    }

    // main optimisation function, this calls the A* planner and extracts and returns the optimal plan.
    public List<bool[]> Plan()
    {
        LevelScene currentState = backupState();
        if (workScene == null)
            workScene = levelScene;

        startSearch(stepsPerSearch);

        search();

        restoreState(currentState);

        return extractPlan();
    }

    /// <summary>
    /// init the planner 
    /// </summary>
    /// <param name="repetitions"> How often an action is repeated. </param>
    // initialise the planner
    private void startSearch(int repetitions)
    {
        // if (levelScene.verbose > 1) System.out.println("Started search."); 
        SearchNode startPos = new SearchNode(null, repetitions, null, this); // passing this AStarSimulater should emulate the java functionality of refering to outer class variables 
        startPos.sceneSnapshot = backupState();

        posPool = new List<SearchNode>();
        visitedStates.Clear();
        foreach (SearchNode node in startPos.generateChildren())
        {
            posPool.Add(node);
        }
        currentSearchStartingMarioXPos = levelScene.plumberXposition;

        bestPosition = startPos;
        furthestPosition = startPos;
    }

    // main search function
    private void search()
    {
        SearchNode current = bestPosition;
        bool currentGood = false;        // is the current node good (= we're not getting hurt)
        int ticks = 0;

        // Search until we've reached the right side of the screen, or if the time is up.
        while (posPool.Count != 0
                && ((bestPosition.sceneSnapshot.plumberXposition - currentSearchStartingMarioXPos < maxRight) || !currentGood))
        //&& (System.currentTimeMillis() - startTime < Math.min(200,timeBudget/2))) <- this makes the game a bit more jerky, but allows a deeper search in tough situations
        {
            ticks++;

            // Pick the best node from our open list
            current = pickBestPos(posPool);
            currentGood = false; // TODO currentGood might not make sense without goombas, it seems to make sense since damage is only gaps
            Debug.Log("Tested Action" + current.action);

            // Simulate the consequences of the action associated with the chosen node
            float realRemainingTime = current.simulatePos();

            // Now act on what we get as remaining time (to some distant goal)

            if (realRemainingTime < 0)
            {
                // kick out negative remaining time (shouldnt happen)
                continue;
            }
            else if (!current.isInVisitedList
                    && isInVisited(current.sceneSnapshot.plumberXposition, current.sceneSnapshot.plumberYposition, current.timeElapsed))
            {
                // if the position & time of the node is already in the closed list
                // (i.e., has been explored before), put some penalty on it and put it 
                // back into the pool. The closed list works approximately: nodes too close
                // to an item in the closed list are considered visited, even though they're a bit different.

                realRemainingTime += visitedListPenalty;
                current.isInVisitedList = true;
                current.remainingTime = realRemainingTime;
                current.remainingTimeEstimated = realRemainingTime;

                posPool.Add(current);
            }
            else if (realRemainingTime - current.remainingTimeEstimated > 0.1)
            {
                // current node is not as good as anticipated. put it back in pool and look for best again
                current.remainingTimeEstimated = realRemainingTime;
                posPool.Add(current);
            }
            else
            {
                // accept the node, its estimated time is as good as its real time.
                currentGood = true;
                Debug.Log("Accepted Action:" + current.action);

                // put it into the visited list 
                // TODO apperently we are ignoring the acceleration
                visited((int)current.sceneSnapshot.plumberXposition, (int)current.sceneSnapshot.plumberYposition, current.timeElapsed);

                // put all children into the open list
                foreach (SearchNode node in current.generateChildren())
                {
                    posPool.Add(node);
                }
            }
            if (currentGood)
            {
                // the current node is the best node (property of A*)
                bestPosition = current;

                // if we're not over a gap, accept it also as the furthest pos.
                // the furthest position is a work-around to avoid falling into gaps
                // when the search is stopped (by time-out) while we're over a gap
                if (current.sceneSnapshot.plumberXposition > furthestPosition.sceneSnapshot.plumberXposition
                        // && !levelScene.level.isGap[(int)(current.sceneSnapshot.plumberXposition / 16)])
                        && !levelScene.isGap(current.sceneSnapshot.plumberXposition))
                    furthestPosition = current;
            }
        }
        // TODO dirty number
        if (levelScene.plumberXposition - currentSearchStartingMarioXPos < maxRight
                && furthestPosition.sceneSnapshot.plumberXposition > bestPosition.sceneSnapshot.plumberXposition + 20
                // && (levelScene.mario.fire ||
                //        levelScene.level.isGap[(int)(bestPosition.sceneSnapshot.mario.x / 16)]))
                && levelScene.isGap(bestPosition.sceneSnapshot.plumberXposition))
        {
            // Couldnt plan till end of screen, take furthest (in some situations)
            bestPosition = furthestPosition;
        }

        // TODO verbose ???
        // if (levelScene.verbose > 1) System.out.println("Search stopped. Remaining pool size: " + posPool.size() + " Current remaining time: " + current.remainingTime);

        levelScene = current.sceneSnapshot;
    }


    // Does the application of the jump action make any difference in the given world state?
    // if not, we don't need to consider it for child positions of nodes
    /// <summary>
    /// We use a simplified version that only checks wether plumber is able to jump (does he touch the ground?)
    /// </summary>
    /// <param name="currentPos"></param>
    /// <param name="checkParent"></param>
    /// <returns></returns>
    public bool canJumpHigher(SearchNode currentPos, bool checkParent)
    {
        // This is a hack to allow jumping one tick longer than required 
        // (because we're planning two steps ahead there might be some odd situations where we might need that)
        if (currentPos.parentPos != null && checkParent
                && canJumpHigher(currentPos.parentPos, false))
            return true;
        // return currentPos.sceneSnapshot.mario.mayJump() || (currentPos.sceneSnapshot.mario.jumpTime > 0);
        return currentPos.sceneSnapshot.PlumberMayJump();
    }

    public static bool[] createAction(bool left, bool right, bool jump)
    {
        bool[] action = new bool[3];
        // action[Mario.KEY_DOWN] = down;
        action[(int)Actions.Jump] = jump;
        action[(int)Actions.Left] = left;
        action[(int)Actions.Right] = right;
        // action[Mario.KEY_SPEED] = speed;
        return action;
    }

    // distance covered at maximum acceleration with initialSpeed for ticks timesteps 
    // this is the closed form of the above function, found using Matlab 
    // TODO redo this completley for our engine
    private float maxForwardMovement(float initialSpeed, int ticks)
    {
        float y = ticks;
        float s0 = initialSpeed;
        return (float)(99.17355373 * Mathf.Pow(0.89f, y + 1)
          - 9.090909091 * s0 * Mathf.Pow(0.89f, y + 1)
          + 10.90909091 * y - 88.26446282 + 9.090909091 * s0);
    }

    // TODO implement damage!! 
    public int getMarioDamage()
    {
        // early damage at gaps: Don't even fall 1 px into them.
        if (levelScene.isGap(levelScene.plumberXposition) &&
                (levelScene.plumberYposition - (MARIOHEIGHT / 2)) > levelScene.gapHeight(levelScene.plumberXposition)) // changed it to smaller since we calculate GapHeight differently, also substract hafl plumber height since the position is in the middle
        {
            levelScene.plumberDamage += 5;
        }
        //Debug.Log("Plumber Damage:" + levelScene.plumberDamage);
        return levelScene.plumberDamage;
    }

    // Extract the plan by taking the best node and going back to the root, 
    // recording the actions at each step.
    private List<bool[]> extractPlan()
    {
        List<bool[]> actions = new List<bool[]>();

        // just move forward if no best position exists
        if (bestPosition == null)
        {
            // TODO verbose
            // if (levelScene.verbose > 1) System.out.println("NO BESTPOS!");
            for (int i = 0; i < 10; i++)
            {
                actions.Add(createAction(false, true, false));
            }
            return actions;
        }
        // TODO verbose
        // if (levelScene.verbose > 2) System.out.print("Extracting plan (reverse order): ");
        SearchNode current = bestPosition;
        while (current.parentPos != null)
        {
            for (int i = 0; i < current.repetitions; i++)
                // actions.Add(0, current.action); // TODO why the 0 ?? I think this is to add it at the beginning
                actions.Insert(0, current.action);
            // TODO verbose
            //if (levelScene.verbose > 2)
            //    System.out.print("["
            //        + (current.action[Mario.KEY_DOWN] ? "d" : "")
            //        + (current.action[Mario.KEY_RIGHT] ? "r" : "")
            //        + (current.action[Mario.KEY_LEFT] ? "l" : "")
            //        + (current.action[Mario.KEY_JUMP] ? "j" : "")
            //        + (current.action[Mario.KEY_SPEED] ? "s" : "")
            //        + (current.hasBeenHurt ? "-" : "") + "]");
            current = current.parentPos;
        }
        // TODO verbose
        // if (levelScene.verbose > 2) System.out.println();
        return actions;
    }

    // pick the best node out of the open list, using the typical A* decision
    // method, which is fitness = elapsed time + estimated time to goal
    private SearchNode pickBestPos(List<SearchNode> posPool)
    {
        SearchNode bestPos = null;
        float bestPosCost = Mathf.Infinity;
        foreach (SearchNode current in posPool)
        {
            // slightly bias towards furthest positions
            float currentCost = current.getRemainingTime()
                + current.timeElapsed * 0.90f;
            if (currentCost < bestPosCost)
            {
                bestPos = current;
                bestPosCost = currentCost;
            }
        }
        posPool.Remove(bestPos);
        return bestPos;
    }

    // make a clone of the current world state (copying marios state, all enemies, and some level information)
    // TODO we removed enemies for now
    public LevelScene backupState()
    {
        LevelScene sceneCopy = (LevelScene)levelScene.Clone();

        return sceneCopy;
    }



    public void restoreState(LevelScene l)
    {
        levelScene = l;
        l.RestoreThisScene();
    }

    public void advanceStep(bool[] action)
    {
        //levelScene.mario.setKeys(action);
        //if (levelScene.verbose > 8) System.out.print("["
        //        + (action[Mario.KEY_DOWN] ? "d" : "")
        //        + (action[Mario.KEY_RIGHT] ? "r" : "")
        //        + (action[Mario.KEY_LEFT] ? "l" : "")
        //        + (action[Mario.KEY_JUMP] ? "j" : "")
        //        + (action[Mario.KEY_SPEED] ? "s" : "") + "]");
        levelScene.Tick(action);
        // TODO can we get a new Tick of mario?
    }

    /// <summary>
    /// visited nodes, ignores acceleration
    /// </summary>
    /// <param name="x"> plumber x position</param>
    /// <param name="y"> plumber y position</param>
    /// <param name="t"> elapsed time </param>
    private void visited(float x, float y, float t)
    {
        visitedStates.Add(new float[] { x, y, t});
    }

    private bool isInVisited(float x, float y, int t)
    {
        // is the (x, y, time) triple too close to a triple in the visited states list?

        foreach (float[] v in visitedStates)
        {
            if (Mathf.Abs(v[0] - x) < xDiff
                    && Mathf.Abs(v[1] - y) < yDiff
                    && Mathf.Abs(v[2] - t) < timeDiff
                    && t >= v[2]) // why do we check if the current node is furhter in time? Oh since only then it can be already visited
            {
                return true;
            }
        }
        return false;
    }

    // estimate the optimal forward movement for a fixed amount of ticks, given a speed and an action
    // This is a bit hacky
    public float[] estimateMaximumForwardMovement(float currentAccel, bool[] action, int ticks)
    {
        float dist = 0;
        // float runningSpeed = action[Mario.KEY_SPEED] ? 1.2f : 0.6f;
        int dir = 0;
        if (action[(int)Actions.Left]) dir = -1;
        if (action[(int)Actions.Right]) dir = 1;
        for (int i = 0; i < ticks; i++)
        {
            // get the acceleration in the x axis
            currentAccel = SharedData.ComputePlayerVelocity(new Vector2(currentAccel, 0), dir, 0, this.tickTime)[0];
            dist += currentAccel;
            // Slow down TODO incorporate our Gravity
            currentAccel *= 0.89f;
        }
        float[] ret = new float[2];
        ret[0] = dist;
        ret[1] = currentAccel;
        return ret;
    }

    // main optimisation function, this calls the A* planner and extracts and returns the optimal action.
    public bool[] optimise()
    {
        // long startTime = System.currentTimeMillis();
        LevelScene currentState = backupState();
        if (workScene == null)
            workScene = levelScene;

        // How many ticks to plan ahead into the future (can be tweaked)
        int planAhead = 2;

        ticksBeforeReplanning--;
        if (ticksBeforeReplanning <= 0 || currentActionPlan.Count == 0)
        {
            // We're done planning, extract the plan and prepare the planner for the
            // next planning iteration (which starts planAhead ticks in the future)
            currentActionPlan = extractPlan();
            if (currentActionPlan.Count < planAhead)
            {
                // if (levelScene.verbose > 2) System.out.println("Warning!! currentActionPlan smaller than planAhead! plansize: " + currentActionPlan.size());
                planAhead = currentActionPlan.Count;
            }

            // simulate ahead to predicted future state, and then plan for this future state 
            // if (levelScene.verbose > 3) System.out.println("Advancing current state ... ");
            for (int i = 0; i < planAhead; i++)
            {
                advanceStep(currentActionPlan[i]);
            }
            workScene = backupState();
            startSearch(stepsPerSearch);
            ticksBeforeReplanning = planAhead;
        }
        // load (future) world state used by the planner
        restoreState(workScene);
        search();
        workScene = backupState();

        // select the next action from our plan
        bool[] action = new bool[3];
        if (currentActionPlan.Count > 0)
            action = currentActionPlan[0];
        currentActionPlan.RemoveAt(0);

        // long e = System.currentTimeMillis();
        // if (levelScene.verbose > 0) System.out.println("Simulation took " + (e - startTime) + "ms.");
        restoreState(currentState);
        return action;
    }


    /*
     * DEPRECATED STUFF
     * 
     * 
     public String printAction(boolean[] action)
    {
        String s = "";
        if (action[Mario.KEY_RIGHT]) s += "Forward ";
        if (action[Mario.KEY_LEFT]) s += "Backward ";
        if (action[Mario.KEY_SPEED]) s += "Speed ";
        if (action[Mario.KEY_JUMP]) s += "Jump ";
        if (action[Mario.KEY_DOWN]) s += "Duck";
        return s;
    }

    public void setLevelPart(byte[][] levelPart, float[] enemies)
    {
        levelScene.setLevelScene(levelPart);
        levelScene.setEnemies(enemies);
    }

    */
}

