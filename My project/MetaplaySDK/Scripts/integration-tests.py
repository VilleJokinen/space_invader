import os
import sys
import glob
import yaml
import shlex
import shutil
import urllib.request
import asyncio
import argparse
import colorama
import traceback
import logging
from timeit import default_timer as get_elapsed_seconds
from typing import Tuple, List

class Color:
  HEADER = '\033[95m'
  OKBLUE = '\033[94m'
  OKCYAN = '\033[96m'
  OKGREEN = '\033[92m'
  WARNING = '\033[93m'
  FAIL = '\033[91m'
  ENDC = '\033[0m'
  BOLD = '\033[1m'
  UNDERLINE = '\033[4m'

# Async OS Process

class AsyncProcess:
  def __init__(self, log: logging.Logger, directory: str, command: str, pipe_stdin: bool = False):
    self.log = log
    self.directory = directory
    self.command = command
    self.pipe_stdin = pipe_stdin
    self.proc = None

  async def run(self):
    self.log.info(f'Run process: {self.command}')
    cmd = shlex.split(self.command)
    executable = shutil.which(cmd[0])
    try:
      self.proc = await asyncio.create_subprocess_exec(
        executable,
        *cmd[1:],
        stdin = asyncio.subprocess.PIPE if self.pipe_stdin else None,
        stdout = asyncio.subprocess.PIPE,
        stderr = asyncio.subprocess.PIPE,
        cwd = self.directory
      )
    except Exception as ex:
      self.log.error(f'{Color.FAIL}ERROR Failed to execute process "{self.command}": {ex}{Color.ENDC}')
      raise

    self.stdout_lines = []
    self.stderr_lines = []

    task_stdout = asyncio.create_task(self.stdout_reader())
    task_stderr = asyncio.create_task(self.stderr_reader())
    await asyncio.gather(task_stdout, task_stderr)

    self.returncode = await self.proc.wait()

  def get_output(self):
    stdout = '\n'.join(self.stdout_lines)
    stderr = '\n'.join(self.stderr_lines)
    return f'<<<\nDIR: {self.directory} COMMAND: {self.command}\n\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}\n>>>'.replace('\r', '')

  async def stdout_reader(self):
    while True:
      line = await self.proc.stdout.readline()
      if not line:
        break
      line = line.decode('utf-8').rstrip()
      self.log.debug(f'  {line}')
      self.stdout_lines.append(line)

  async def stderr_reader(self):
    while True:
      line = await self.proc.stderr.readline()
      if not line:
        break
      line = line.decode('utf-8').rstrip()
      self.log.debug(f'  {line}')
      self.stderr_lines.append(line)

async def run_process(task_name: str, directory: str, command: str, pipe_stdin: bool = False):
  proc = AsyncProcess(task_name, directory, command, pipe_stdin)
  await proc.run()
  return proc

### Parse command line arguments

parser = argparse.ArgumentParser(description='Metaplay automated integration test runner')
parser.add_argument('--project-dir', type=str, default='.', help='Path to project directory, relative to working directory')
parser.add_argument('--backend-dir', type=str, default='Backend', help='Path to project backend directory, relative to --project-dir')
parser.add_argument('--results-dir', type=str, default='results', help='Base directory for outputs from the test run (eg, Cypress screenshots), relative to working directory')
parser.add_argument('--name-prefix', type=str, default='metatest', help='Prefix string to use for docker images and containers')
parser.add_argument('--use-buildkit', default=False, action='store_true', help='Use legacy docker BuildKit instead of the more modern buildx. At least Bitbucket seems to have spotty support for buildx.')
parser.add_argument('-q', '--quiet', default=False, action='store_true', help='Run quietly, don\'t log the outputs from the process invocations.')
parser.add_argument('tests', nargs='*', help='List of tests to run, default is to run all tests')
args = parser.parse_args()

# If using BuildKit, set environment variable DOCKER_BUILDKIT=1 globally
if args.use_buildkit:
  print('Using Docker BuildKit')
  os.environ['DOCKER_BUILDKIT'] = '1'

SERVER_IMAGE_NAME = f'{args.name_prefix}-server:test'
DASHBOARD_IMAGE_NAME = f'{args.name_prefix}-dashboard:test'
SERVER_CONTAINER_NAME = f'{args.name_prefix}-server'
BOTCLIENT_CONTAINER_NAME = f'{args.name_prefix}-botclient'
DASHBOARD_CONTAINER_NAME = f'{args.name_prefix}-dashboard'

METAPLAY_SDK_DIR = os.path.relpath(os.path.join(os.path.dirname(__file__), '..'), '.').replace('\\','/')
PROJECT_BACKEND_DIR = os.path.join(args.project_dir, args.backend_dir).replace('\\', '/')

if not os.path.exists(os.path.join(args.project_dir, 'Assets')):
  parser.error(f'Unable to find "{args.project_dir}/Assets", make sure your --project-dir is correct!')

if not os.path.exists(os.path.join(PROJECT_BACKEND_DIR, 'Dashboard')):
  parser.error(f'Unable to find project backend in "{args.project_dir}/{args.backend_dir}", make sure your --backend-dir is correct!')

METAPLAY_OPTS = '--Environment:EnableKeyboardInput=false'
METAPLAY_SERVER_OPTS = '--Environment:EnableSystemHttpServer=true --Environment:SystemHttpListenHost=0.0.0.0 --AdminApi:WebRootPath=wwwroot'

## Helper methods

def validateYamlFiles(log: logging.Logger, globs):
  for path in globs:
    matches = glob.glob(path)
    log.debug(f'Checking path: {path}')
    for file_path in matches:
      log.debug(f'  {file_path}')
      try:
        with open(file_path) as file:
          _ = yaml.full_load(file)
      except:
        raise Exception(f'Failed to parse YAML file: {file_path}')

    if len(matches) == 0:
        raise Exception(f'Failed find any YAML file with {path}')

async def httpGetRequest(log: logging.Logger, url: str) -> str:
  # \todo [petri] make async
  response = urllib.request.urlopen(url)
  log.debug(f'{url} returned {response.getcode()}')
  if response.getcode() >= 200 and response.getcode() < 300:
    return response.read().decode('utf-8')
  else:
    raise Exception(f'Got code {response.getcode()} when requesting url {url}')

def parsePrometheusMetric(line: str) -> Tuple[str, float]:
  [name, value] = line.split(' ')
  return (name, float(value))

async def fetchPrometheusMetrics(log: logging.Logger, url: str) -> List[Tuple[str, float]]:
  response = await httpGetRequest(log, url)
  lines = response.split('\n')
  lines = [line for line in lines if not line.startswith('#') and line != '']
  # if DEBUG:
  #   print('\n'.join(lines))
  filtered = [line for line in lines if line.startswith('dotnet_cpu_time_total') or line.startswith('process_cpu_seconds_total') or line.startswith('game_connections_current')]
  # process_cpu_seconds_total
  # dotnet_memory_virtual
  # dotnet_memory_allocated
  # dotnet_total_memory_bytes
  # dotnet_gc_loh_size
  # game_connections_current{type="Tcp"} 0
  # dotnet_collection_count_total{generation="1"} 1
  # dotnet_collection_count_total{generation="2"} 1
  # dotnet_collection_count_total{generation="0"} 1
  metrics = {}
  for (name, value) in [parsePrometheusMetric(line) for line in filtered]:
    metrics[name] = value
  return metrics

async def testHttpSuccess(log: logging.Logger, url: str):
  try:
    response = urllib.request.urlopen(url)
    log.debug(f'{url} returned {response.getcode()}')
    if response.getcode() >= 200 and response.getcode() < 300:
      return True
  except Exception as e:
    log.debug(f'Failed to fetch {url}: {e}')
  return False

async def httpPostRequest(log: logging.Logger, url: str, data=b''):
  try:
    # \todo [petri] make async
    request = urllib.request.Request(url, data=data) # provide data to use a POST
    response = urllib.request.urlopen(request)
    log.debug(f'{url} returned {response.getcode()}')
    if response.getcode() >= 200 and response.getcode() < 300:
      return True
  except Exception as e:
    log.error(f'Failed HTTP POST to {url}: {e}')
  return False

async def runDockerTask(log: logging.Logger, command: str):
  proc = await run_process(log, directory='.', command=command, pipe_stdin=False)
  if proc.returncode != 0:
    print(f'{Color.FAIL}Docker command "{command}" exited with code {proc.returncode}:{Color.ENDC}\n{proc.get_output()}')
    raise Exception(f'Docker task "{command}" exited with code {proc.returncode}')
  return proc

async def runDockerBuildTask(log: logging.Logger, command: str):
  cmd_prefix = 'docker build' if args.use_buildkit else 'docker buildx build --output=type=docker'
  await runDockerTask(log.getChild('server'), f'{cmd_prefix} {command}')

async def killDockerContainer(log: logging.Logger, container_name: str):
  try:
    _ = await run_process(log, directory='.', command=f'docker kill {container_name}', pipe_stdin=False)
  except:
    pass

class BackgroundGameServer:
  def __init__(self, log, server_proc):
    self.log = log
    self.server_proc = server_proc
    self.server_task = asyncio.create_task(server_proc.run(), name='run-gameserver') # create task so process makes progress in background
    self.metrics_task = None
    self.metrics_samples = []
    self.stop_event = asyncio.Event()

  async def _collectMetricsAsync(self):
    prewarm_time = 30.0 # wait 30sec until start accumulating metrics (initial)
    start_time = get_elapsed_seconds()
    prev_time = start_time
    prev_cpu_time_total = 0.0
    while not self.stop_event.is_set():
      try:
        try:
          await asyncio.wait_for(self.stop_event.wait(), timeout=5.0)
        except asyncio.TimeoutError:
          pass
        cur_time = get_elapsed_seconds()
        metrics = await fetchPrometheusMetrics(self.log, 'http://localhost:9090/metrics')
        # print(metrics)
        cpu_time_total = metrics['process_cpu_seconds_total']
        concurrents = sum([metrics[name] for name in metrics if name.startswith('game_connections_current')])
        if concurrents >= 10:
          time_elapsed = cur_time - prev_time
          cpu_usage_cores = (cpu_time_total - prev_cpu_time_total) / time_elapsed # number of cores busy (per second)
          concurrents_per_cpu = concurrents / cpu_usage_cores
          use_sample = prev_time - start_time >= prewarm_time # collect samples after pre-warm time has passed
          self.log.info(f'[{cur_time - start_time:.1f}s] Concurrents={int(concurrents)} CPU={cpu_usage_cores:.3f}cores/s CCU/core={concurrents_per_cpu:0.1f} ({"use" if use_sample else "skip"})')
          if use_sample:
            self.metrics_samples.append((time_elapsed, concurrents, cpu_usage_cores))

        prev_time = cur_time
        prev_cpu_time_total = cpu_time_total
      except Exception as e:
        self.log.error(f'ERROR: Exception caught while collecting metrics: {e}')
        traceback.print_exc()

  def startCollectingMetrics(self):
    self.metrics_task = asyncio.create_task(self._collectMetricsAsync())

  def summarizeMetrics(self):
    num_samples = len(self.metrics_samples)
    if num_samples == 0:
        self.log.info(f'***** Samples={num_samples} Concurrents=(n/a) CPU=(n/a)cores CCU/core=(n/a)')
        return

    total_time_elapsed = 0.0
    total_concurrents = 0
    total_cpu_usage_cores = 0.0
    for (time_elapsed, concurrents, cpu_usage_cores) in self.metrics_samples:
      total_time_elapsed += time_elapsed
      total_concurrents += concurrents
      total_cpu_usage_cores += cpu_usage_cores
    avg_concurrents = total_concurrents / num_samples
    avg_cpu_usage_cores = total_cpu_usage_cores / num_samples
    concurrents_per_core = avg_concurrents / avg_cpu_usage_cores
    self.log.info(f'***** Samples={num_samples} Concurrents={avg_concurrents:.1f} CPU={total_cpu_usage_cores:.2f}cores CCU/core={concurrents_per_core}')

  async def waitForReady(self):
    # Wait for server /isReady to return success
    while True:
      self.log.debug('Check server up')
      if await testHttpSuccess(self.log, 'http://localhost:8888/isReady'):
        self.log.info(f'Server is ready!')
        break
      else:
        # Check if server died unexpectedly during init
        if self.server_task.done():
          self.log.error(f'Server exited unexpectedly while waiting for it to be ready: <<<{self.server_proc.get_output()}>>>')
          raise Exception('Server exited unexpectedly while waiting for it to be ready!')
        await asyncio.sleep(0.2)

  async def waitFinished(self):
    await asyncio.wait([self.server_task])

  async def stop(self):
    self.stop_event.set()

    # wait for metrics collection to stop
    await asyncio.wait([self.metrics_task])

    # print('Sending SIGTERM')
    # self.server_proc.proc.terminate()
    # await asyncio.sleep(2)
    self.log.info('Requesting gameserver graceful shutdown')
    await httpPostRequest(self.log, 'http://localhost:8888/gracefulShutdown')
    self.log.info('Killing docker container') # \todo [petri] use SIGTERM instead?
    await runDockerTask(self.log, f'docker kill {SERVER_CONTAINER_NAME}')
    self.log.info('Waiting for gameserver to exit')
    await self.waitFinished()

async def startGameServer(log: logging.Logger):
  # Kill old server in case it exists
  await killDockerContainer(log, SERVER_CONTAINER_NAME)

  # Start the server
  log.info('Start game server container')
  server_proc = AsyncProcess(log, directory='.', command=f'docker run --rm --name {SERVER_CONTAINER_NAME} -e METAPLAY_ENVIRONMENT_FAMILY=Local -p 8888:8888 -p 9090:9090 {SERVER_IMAGE_NAME} gameserver -LogLevel=Information {METAPLAY_OPTS} {METAPLAY_SERVER_OPTS}', pipe_stdin=False)
  gameserver = BackgroundGameServer(log, server_proc)

  # Wait until server is ready & start collecting metrics
  await gameserver.waitForReady()
  gameserver.startCollectingMetrics()
  return gameserver

async def runBotClient(log: logging.Logger, duration: str, max_bots: int, spawn_rate: int, session_duration: str) -> None:
  await killDockerContainer(log, BOTCLIENT_CONTAINER_NAME)
  await runDockerTask(log, f'docker run --rm --name {BOTCLIENT_CONTAINER_NAME} --network container:{SERVER_CONTAINER_NAME} -e METAPLAY_ENVIRONMENT_FAMILY=Local {SERVER_IMAGE_NAME} botclient -LogLevel=Information {METAPLAY_OPTS} --Bot:ServerHost=localhost --Bot:ServerPort=9339 --Bot:EnableTls=false --Bot:CdnBaseUrl=http://localhost:5552/ -ExitAfter={duration} -MaxBots={max_bots} -SpawnRate={spawn_rate} -ExpectedSessionDuration={session_duration}')

async def runCypressTests(log: logging.Logger):
  await killDockerContainer(log, DASHBOARD_CONTAINER_NAME)
  RESULTS_DIR = os.path.abspath(args.results_dir).replace('\\', '/')
  await runDockerTask(log, f'docker run --rm --name {DASHBOARD_CONTAINER_NAME} --network container:{SERVER_CONTAINER_NAME} -v {RESULTS_DIR}/cypress:/build/{PROJECT_BACKEND_DIR}/Dashboard/cypress {DASHBOARD_IMAGE_NAME} npx cypress run --browser electron --config baseUrl=http://localhost:5550')

## Tests

# TEST CASE: Build images

async def testBuildImage(log: logging.Logger):
  # Ensure all runtime option .yaml files valid
  validateYamlFiles(log.getChild('validate-yaml'), [os.path.join(PROJECT_BACKEND_DIR, 'Server/Config/*.yaml'), os.path.join(PROJECT_BACKEND_DIR, 'BotClient/Config/*.yaml')])

  # Build server image
  docker_build_args = f'--pull --build-arg SDK_ROOT={METAPLAY_SDK_DIR} --build-arg PROJECT_ROOT={args.project_dir} --build-arg BACKEND_DIR={args.backend_dir} --build-arg RUN_TESTS=1 -f {METAPLAY_SDK_DIR}/Dockerfile.server'
  await runDockerBuildTask(log.getChild('server'), f'-t {SERVER_IMAGE_NAME} {docker_build_args} .')

  # Build dashboard image
  await runDockerBuildTask(log.getChild('dashboard'), f'-t {DASHBOARD_IMAGE_NAME} --target build-dashboard {docker_build_args} .')

  # Start/stop server
  # gameserver = await startGameServer(log.getChild('server'))
  # await gameserver.stop()

# TEST CASE: Run bots

async def testBots(log: logging.Logger):
  gameserver = await startGameServer(log.getChild('server'))
  try:
    await runBotClient(log.getChild('bots'), duration='00:02:00', max_bots=300, spawn_rate=30, session_duration='00:00:30')
    gameserver.summarizeMetrics()
  finally:
    await gameserver.stop()

# TEST CASE: Dashboard (Cypress) tests

async def testDashboard(log: logging.Logger):
  gameserver = await startGameServer(log.getChild('server'))
  try:
    await runCypressTests(log.getChild('dashboard'))
  finally:
    await gameserver.stop()

## Main

TEST_SPECS = [
  ('build-image', testBuildImage),
  ('test-bots', testBots),
  ('test-dashboard', testDashboard),
]

# Configure logging
logging.basicConfig(
  level=logging.INFO if args.quiet else logging.DEBUG,
  format='[%(asctime)s.%(msecs)03d %(levelname)s %(name)s %(funcName)s:%(lineno)d] %(message)s',
  datefmt='%Y-%m-%d %H:%M:%S')

async def main():
  for (test_name, test_fn) in TEST_SPECS:
    log = logging.getLogger(test_name)
    should_run = len(args.tests) == 0 or test_name in args.tests
    if should_run:
      try:
        log.info(f'Running test: {test_name}')
        await test_fn(log)
        log.info(f'{Color.OKGREEN}Test {test_name} success{Color.ENDC}')
      except Exception as e:
        log.error(f'{Color.FAIL}Test {test_name} failed with: {e}{Color.ENDC}')
        traceback.print_exc() # print the stack trace so we know what failed
        sys.exit(1)
    else:
      log.warning(f'Skip test: {test_name}')

if __name__ == '__main__':
  colorama.init()
  try:
    asyncio.run(main())
  except Exception as e:
    print(e.message)
