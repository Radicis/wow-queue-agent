/**
 * @format
 */

import { AppRegistry } from 'react-native';
import App from './App';
import { name as appName } from './app.json';
import { registerForegroundTask } from './src/notifications';

registerForegroundTask();

AppRegistry.registerComponent(appName, () => App);
