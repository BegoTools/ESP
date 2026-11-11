import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.lang.reflect.Method;

/** Standalone Android shell input client. Runs outside the game process. */
public final class EspTouchMain {
    private final Object manager;
    private final Method obtain, setSource, setDisplay, recycle, inject, uptime;
    private final int display, width, height;
    private float x, y;
    private long downTime, lastMotion, lastCommand, lastWarning;
    private boolean down, stopped;

    private EspTouchMain(int display, int width, int height) throws Exception {
        this.display=display;this.width=width;this.height=height;
        if(display<0 || width<100 || height<100 || width>10000 || height>10000)throw new IllegalArgumentException("Invalid display");
        Class<?> motion=Class.forName("android.view.MotionEvent");
        Class<?> inputManager=Class.forName("android.hardware.input.InputManager");
        manager=inputManager.getMethod("getInstance").invoke(null);
        inject=inputManager.getMethod("injectInputEvent",Class.forName("android.view.InputEvent"),int.class);
        obtain=motion.getMethod("obtain",long.class,long.class,int.class,float.class,float.class,int.class);
        setSource=motion.getMethod("setSource",int.class);
        setDisplay=Class.forName("android.view.InputEvent").getMethod("setDisplayId",int.class);
        recycle=motion.getMethod("recycle");
        uptime=Class.forName("android.os.SystemClock").getMethod("uptimeMillis");
        lastCommand=now();
    }
    private long now() throws Exception { return (Long)uptime.invoke(null); }
    private void event(int action,long time) throws Exception {
        Object motion=obtain.invoke(null,downTime,time,action,x,y,0);
        try {
            setSource.invoke(motion,0x1002);setDisplay.invoke(motion,display);
            if(!((Boolean)inject.invoke(manager,motion,2)))throw new IllegalStateException("Android rejected touch event");
        } finally { recycle.invoke(motion); }
    }
    private synchronized void release() throws Exception {
        if(down) { try { event(1,now()); } finally { down=false; } }
    }
    private synchronized void move(float dx,float dy) throws Exception {
        if(!Float.isFinite(dx)||!Float.isFinite(dy)||Math.abs(dx)>40||Math.abs(dy)>40)throw new IllegalArgumentException("Invalid delta");
        long time=now();
        // Complete each short gesture before accepting another correction. This avoids
        // holding a competing device pointer while GameLoop translates physical input.
        release();x=width*.65f;y=height*.5f;downTime=time;down=true;
        try {
            event(0,time);
            float startX=x,startY=y;
            // Keep the drag alive across game frames. A down/move/up completed in
            // one frame is treated as a tap by this game, despite successful delivery.
            for(int step=1;step<=3;step++) {
                Thread.sleep(8);x=startX+dx*step/3;y=startY+dy*step/3;event(2,now());
            }
            Thread.sleep(8);lastMotion=now();
        } finally { release(); }
    }
    private void run(String session) throws Exception {
        Thread watchdog=new Thread(() -> {
            try {
                while(!stopped) {
                    Thread.sleep(10);
                    synchronized(this) {
                        long time=now();
                        if(down && time-lastMotion>120)release();
                        if(time-lastCommand>2000) { release();System.exit(0); }
                    }
                }
            } catch(Throwable error) { System.err.println(error.toString());System.exit(2); }
        },"touch-watchdog");
        watchdog.setDaemon(true);watchdog.start();
        System.out.println("READY 1 "+session);System.out.flush();
        try(BufferedReader reader=new BufferedReader(new InputStreamReader(System.in))) {
            String line;
            while((line=reader.readLine())!=null) {
                synchronized(this) { lastCommand=now(); }
                if(line.equals("PING"))continue;
                String[] parts=line.split(" ");
                if(parts.length==4 && parts[0].equals("M")) {
                    long sequence=Long.parseLong(parts[1]);
                    try {
                        move(Float.parseFloat(parts[2]),Float.parseFloat(parts[3]));
                        System.out.println("OK "+sequence);
                    } catch(java.lang.reflect.InvocationTargetException rejected) {
                        if(now()-lastWarning>2000) { System.err.println("Touch conflict: "+rejected.getCause());lastWarning=now(); }System.out.println("SKIP "+sequence);
                    } catch(IllegalStateException rejected) {
                        if(now()-lastWarning>2000) { System.err.println("Touch conflict: "+rejected);lastWarning=now(); }System.out.println("SKIP "+sequence);
                    }
                    System.out.flush();
                } else if(parts.length==2 && parts[0].equals("U")) {
                    release();System.out.println("UP "+parts[1]);System.out.flush();
                } else throw new IllegalArgumentException("Unknown command");
            }
        } finally { synchronized(this) { stopped=true;release(); } }
    }
    public static void main(String[] args) {
        try {
            if(args.length!=4 || !args[0].matches("[a-f0-9]{32}"))throw new IllegalArgumentException("Invalid arguments");
            new EspTouchMain(Integer.parseInt(args[1]),Integer.parseInt(args[2]),Integer.parseInt(args[3])).run(args[0]);
        } catch(Throwable error) { error.printStackTrace(System.err);System.exit(1); }
    }
}
