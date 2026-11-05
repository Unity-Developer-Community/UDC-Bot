import { startApp } from '@/app';
import { createCasinoConnection } from '@/connection';

import './style.css';

const root = document.querySelector<HTMLElement>('#app');

if (root)
  void startApp(root, { connection: createCasinoConnection() });
