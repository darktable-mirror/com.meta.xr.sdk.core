/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

use std::path::PathBuf;

use anyhow::Context;
use anyhow::Result;
use anyhow::bail;
use perfagent_metric_api::process_trace;

#[tokio::main]
async fn main() -> Result<()> {
    let mut arguments: Vec<String> = std::env::args()
        .skip(1)
        .filter(|argument| argument != "--log")
        .collect();
    let (trace, processor, output) = match arguments.len() {
        2 => {
            let output = arguments.pop().expect("argument count was checked");
            let trace = arguments.pop().expect("argument count was checked");
            (
                PathBuf::from(trace),
                resolve_processor()?,
                PathBuf::from(output),
            )
        }
        3 => {
            let output = arguments.pop().expect("argument count was checked");
            let processor = arguments.pop().expect("argument count was checked");
            let trace = arguments.pop().expect("argument count was checked");
            (
                PathBuf::from(trace),
                PathBuf::from(processor),
                PathBuf::from(output),
            )
        }
        _ => bail!("usage: MetricAPI <tracePath> [binPath] <jsonPath> [--log]"),
    };
    let metrics = process_trace(trace, processor).await?;
    std::fs::write(&output, serde_json::to_vec(&metrics)?)
        .with_context(|| format!("failed to write {}", output.display()))?;
    Ok(())
}

fn resolve_processor() -> Result<PathBuf> {
    for variable in [
        "TRACE_PROCESSOR_PATH",
        "HZDB_TRACE_PROCESSOR_PATH",
        "MQDH_TRACE_PROCESSOR_PATH",
    ] {
        if let Some(path) = std::env::var_os(variable).map(PathBuf::from)
            && path.exists()
        {
            return Ok(path);
        }
    }
    bail!("trace_processor_shell path is required or must be set in TRACE_PROCESSOR_PATH")
}
