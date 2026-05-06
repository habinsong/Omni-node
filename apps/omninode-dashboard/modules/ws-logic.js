export function requestLogicGraphList(send, options = {}) {
  return send({ type: "logic_graph_list" }, options)
}

export function requestLogicGraphGet(send, graphId, options = {}) {
  return send({ type: "logic_graph_get", graphId }, options)
}

export function requestLogicPathList(send, scope, rootKey, filePath = "", options = {}) {
  return send({
    type: "logic_path_list",
    scope,
    target: rootKey,
    filePath
  }, options)
}

export function requestLogicGraphSave(send, graphId, graph, options = {}) {
  return send({
    type: "logic_graph_save",
    graphId,
    logicGraph: graph
  }, options)
}

export function requestLogicGraphDelete(send, graphId, options = {}) {
  return send({ type: "logic_graph_delete", graphId }, options)
}

export function requestLogicGraphRun(send, graphId, runInput, options = {}) {
  return send({ type: "logic_graph_run", graphId, logicRunInput: runInput || "" }, options)
}

export function requestLogicGraphRunGet(send, runId, options = {}) {
  return send({ type: "logic_graph_run_get", runId }, options)
}

export function requestLogicGraphCancel(send, runId, options = {}) {
  return send({ type: "logic_graph_cancel", runId }, options)
}
